using DreamPovnota.Web.Import;

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

    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => TypedResults.Text("ok")).ExcludeFromDescription();
        app.MapGet("/health/ready", (EditionStore store) =>
            {
                var current = store.Current;
                var status = store.Status;
                var body = new
                {
                    edition = new
                    {
                        loaded = current is not null,
                        generated = current?.Edition.Generated,
                        lastChecked = status.LastChecked,
                        lastError = status.LastError,
                    },
                };
                return current is null
                    ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Json(body);
            })
            .ExcludeFromDescription();
    }
}
