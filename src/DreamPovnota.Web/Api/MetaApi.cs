using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using Microsoft.Extensions.Caching.Memory;

namespace DreamPovnota.Web.Api;

public static class MetaApi
{
    public static void MapMetaApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/meta", (EditionStore store) =>
            {
                var e = store.Require().Edition;
                return TypedResults.Ok(new MetaInfo(
                    new EditionInfo(e.Generated, e.Synced, e.Cards.Count),
                    e.Checks.Select(c => new CheckInfo(c.Code, c.Title, c.Group, c.CountsInScore, c.From, c.Until, c.Condition, c.Why)).ToList(),
                    e.Regions.Select(r => new KeyTitle(r.Key, r.Title)).ToList(),
                    e.Stages.Select(s => new KeyTitle(s.Key, s.Title)).ToList(),
                    e.Sectors.Select(s => new KeyTitle(s.Key, s.Title)).ToList()));
            })
            .WithTags("Довідка")
            .WithSummary("Випуск даних, перевірки, області, стадії й сектори")
            .CacheOutput(ImportService.CacheTag);
    }

    /// <summary>Як довго /health/ready не питає базу вдруге.</summary>
    public static readonly TimeSpan ReadyCacheFor = TimeSpan.FromSeconds(10);

    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => TypedResults.Text("ok")).ExcludeFromDescription();
        // база необов'язкова: без неї сервіс готовий, тільки без історії
        app.MapGet("/health/ready", async (EditionStore store, HistoryReader history, IMemoryCache cache, CancellationToken ct) =>
            {
                var current = store.Current;
                var status = store.Status;
                // базу питаємо не частіше ніж раз на 10 с: часте опитування не займе пул на 5 з'єднань
                var snapshot = !history.IsConfigured ? null : await cache.GetOrCreateAsync("health:snapshot", entry =>
                {
                    entry.Size = 1;
                    entry.AbsoluteExpirationRelativeToNow = ReadyCacheFor;
                    return history.LastSnapshotAsync(ct);
                });
                var body = new
                {
                    edition = new
                    {
                        loaded = current is not null,
                        generated = current?.Edition.Generated,
                        lastChecked = status.LastChecked,
                        lastError = status.LastError,
                    },
                    database = !history.IsConfigured ? "not-configured" : snapshot is null ? "unavailable" : "ok",
                    lastSnapshot = snapshot?.Generated,
                };
                return current is null
                    ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Json(body);
            })
            .ExcludeFromDescription();
    }
}
