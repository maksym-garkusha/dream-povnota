using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages.Shared;

/// <summary>
/// Блок історії на сторінках організації й області. Якщо база недоступна,
/// сторінку не кешуємо, щоб пояснення не висіло годину після того, як база повернеться.
/// </summary>
public sealed record HistoryView(
    HistoryStatus? Status,
    IReadOnlyList<CardChange> Changes,
    IReadOnlyList<TrendPoint> Trend,
    DateTimeOffset StateDate,
    string FeedPath,
    IReadOnlyDictionary<string, string> Titles)
{
    public const int TrendRows = 30;

    public bool Available => Status is not null;

    public static async Task<HistoryView> LoadAsync(
        HttpContext context, HistoryReader history, LoadedEdition edition, HistoryScope scope,
        Func<CancellationToken, Task<IReadOnlyList<TrendPoint>?>> trend, string feedPath, CancellationToken ct)
    {
        var date = edition.Edition.Synced ?? edition.Edition.Generated;
        var titles = Feeds.AtomFeed.Titles(edition);
        var status = await history.StatusAsync(ct);
        var changes = status is null ? null : await history.ChangesAsync(scope, 1, ct, status.LastId);
        var points = changes is null ? null : await trend(ct);

        if (status is null || changes is null || points is null)
        {
            if (context.Features.Get<IOutputCacheFeature>() is { } cache) cache.Context.AllowCacheStorage = false;
            return new HistoryView(null, [], [], date, feedPath, titles);
        }
        return new HistoryView(status, changes, points.TakeLast(TrendRows).Reverse().ToList(), date, feedPath, titles);
    }
}
