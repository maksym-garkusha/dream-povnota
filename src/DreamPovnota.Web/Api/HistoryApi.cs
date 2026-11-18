using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;

namespace DreamPovnota.Web.Api;

public sealed record ChangesResponse(SnapshotInfo? Snapshot, IReadOnlyList<CardChange> Items);

public sealed record CardWithHistory(CardItem Card, bool HistoryAvailable, IReadOnlyList<CardHistoryPoint>? History);

public static class HistoryApi
{
    public static void MapHistoryApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Історія").CacheOutput(ImportService.CacheTag);

        api.MapGet("/snapshots", async (HistoryReader history, CancellationToken ct) =>
                await history.SnapshotsAsync(1000, ct) is { } list ? Results.Ok(list) : Unavailable())
            .WithSummary("Знімки історії, найновіший перший");

        api.MapGet("/changes", Changes)
            .WithSummary("Зміни карток у знімку порівняно з попереднім: snapshot (типово останній), org або region");

        api.MapGet("/orgs/{key}/trend", async (string key, EditionStore store, HistoryReader history, CancellationToken ct) =>
            {
                key = OrganisationSearch.NormaliseKey(key);
                if (!store.Require().OrganisationsByKey.ContainsKey(key)) return ApiMapping.NotFound("Організації з таким ключем серед відкритих карток немає");
                return await history.OrganisationTrendAsync(key, ct) is { } trend ? Results.Ok(trend) : Unavailable();
            })
            .WithSummary("Наповненість організації за знімками");

        api.MapGet("/regions/{key:regex(^(\\d{{2}}|multi|none)$)}/trend", async (string key, HistoryReader history, CancellationToken ct) =>
                await history.SliceTrendAsync('r', key, ct) is { } trend ? Results.Ok(trend) : Unavailable())
            .WithSummary("Наповненість області за знімками");

        api.MapGet("/cards/{code:regex(^\\d{{6}}-[0-9A-Fa-f]{{8}}$)}/history", async (string code, EditionStore store, HistoryReader history, CancellationToken ct) =>
            {
                var edition = store.Require();
                if (!edition.CardIndexByCode.TryGetValue(code.ToUpperInvariant(), out int index))
                    return ApiMapping.NotFound("Серед відкритих карток поточного випуску такої немає");
                var points = await history.CardAsync(code.ToUpperInvariant(), ct);
                return Results.Ok(new CardWithHistory(ApiMapping.Card(edition, edition.Edition.Cards[index]), points is not null, points));
            })
            .WithSummary("Картка разом з її версіями в історії");
    }

    private static async Task<IResult> Changes(string? snapshot, string? org, string? region, EditionStore store, HistoryReader history, CancellationToken ct)
    {
        var edition = store.Require();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        int? number = null;
        if (snapshot is not null)
        {
            if (int.TryParse(snapshot, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0) number = n;
            else errors["snapshot"] = ["Номер знімка: ціле число від 1"];
        }
        if (org is not null && !edition.OrganisationsByKey.ContainsKey(org)) errors["org"] = ["Такої організації серед відкритих карток немає"];
        if (region is not null && !edition.RegionIndex.ContainsKey(region)) errors["region"] = ["Такої області в даних немає"];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors, title: "Неправильні параметри");

        var snapshots = await history.SnapshotsAsync(1000, ct);
        if (snapshots is null) return Unavailable();
        var chosen = number is null ? snapshots.FirstOrDefault() : snapshots.FirstOrDefault(s => s.Id == number);
        if (chosen is null) return ApiMapping.NotFound("Такого знімка немає");

        var changes = await history.ChangesAsync(new HistoryScope(org, region), 1, ct, chosen.Id);
        return changes is null ? Unavailable() : Results.Ok(new ChangesResponse(chosen, changes));
    }

    public static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Історія тимчасово недоступна",
        detail: "База історії не відповідає або не підключена. Поточний стан доступний у /api/v1/cards.",
        extensions: new Dictionary<string, object?> { ["retryAfter"] = 300 });
}
