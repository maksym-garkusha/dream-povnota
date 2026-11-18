using System.Globalization;
using DreamPovnota.Digest;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;

namespace DreamPovnota.Web.Api;

/// <summary>Файли зрізу для служб відновлення і стан карток на початок тижня для CLI.</summary>
public static class DigestApi
{
    /// <summary>Зріз адресний і не для пошуковиків; той самий заголовок, що й meta robots на сторінках.</summary>
    public const string NoIndex = "noindex, nofollow";

    private static readonly DateOnly Earliest = new(2026, 1, 1);

    public static void MapDigest(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/digest/{code:regex(^[0-9]{{8}}$)}.xlsx", [HttpMethods.Get, HttpMethods.Head], (string code, HttpContext context, EditionStore store, DigestFiles files, CancellationToken ct) =>
                FileAsync(code, "xlsx", context, store, files, ct))
            .ExcludeFromDescription();
        app.MapMethods("/digest/{code:regex(^[0-9]{{8}}$)}.pdf", [HttpMethods.Get, HttpMethods.Head], (string code, HttpContext context, EditionStore store, DigestFiles files, CancellationToken ct) =>
                FileAsync(code, "pdf", context, store, files, ct))
            .ExcludeFromDescription();

        app.MapGet("/api/v1/digest/baseline", Baseline)
            .WithTags("Зріз для служб відновлення")
            .CacheOutput(ImportService.CacheTag)
            .WithSummary("Картки агентства й служб відновлення на початок тижня з історії")
            .WithDescription("from: перший день тижня, yyyy-MM-dd (типово сім днів до поточного випуску). " +
                             "Береться останній знімок не пізніше початку цього дня, а якщо історія молодша, перший знімок.");
    }

    private static async Task<IResult> FileAsync(string code, string format, HttpContext context, EditionStore store, DigestFiles files, CancellationToken ct)
    {
        if (DigestOrganisations.Find(code) is null) return Results.NotFound();
        var entry = (await files.GetAsync(store.Require(), ct)).Find(code)!;
        var file = format == "pdf" ? entry.Pdf : entry.Xlsx;
        context.Response.Headers["X-Robots-Tag"] = NoIndex;
        return TypedResults.File(file.Content, file.ContentType, file.DownloadName, enableRangeProcessing: false, file.LastModified, file.ETag);
    }

    private static async Task<IResult> Baseline(string? from, EditionStore store, HistoryReader history, CancellationToken ct)
    {
        var edition = store.Require().Edition;
        var day = DigestBuilder.WeekFrom(edition);
        if (from is not null
            && !(DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                 && day >= Earliest && day <= DigestBuilder.WeekTo(edition)))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["from"] = [$"Дата yyyy-MM-dd від {Earliest:yyyy-MM-dd} до дати поточного випуску"] },
                title: "Неправильні параметри");
        }

        var codes = DigestOrganisations.All.Select(o => o.Edrpou).ToList();
        var baseline = await history.DigestBaselineAsync(codes, new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), ct);
        return baseline is null ? HistoryApi.Unavailable() : Results.Ok(baseline);
    }
}
