using System.Globalization;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace DreamPovnota.Web.Api;

public static class CardsApi
{
    public static void MapCardsApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Картки").CacheOutput(ImportService.CacheTag);

        api.MapGet("/cards", Cards)
            .WithSummary("Відкриті картки з фільтром")
            .WithDescription("Параметри: q (назва або код), region, org, stage, sector, missing (код перевірки), seeks=1, page, pageSize до 500.");
        api.MapGet("/cards/{code:regex(^\\d{{6}}-[0-9A-Fa-f]{{8}}$)}", Card)
            .WithSummary("Одна картка за кодом без DREAM-UA-");
    }

    public const string ExportCachePolicy = "export";

    public static void MapExports(this IEndpointRouteBuilder app)
    {
        foreach (string prefix in new[] { "/export", "/api/v1" })
        {
            var group = app.MapGroup(prefix).WithTags("Вивантаження").CacheOutput(ExportCachePolicy);
            // HEAD теж: його шлють менеджери завантажень і Power BI перед тим, як качати
            group.MapMethods("/cards.csv", [HttpMethods.Get, HttpMethods.Head], ExportAsync("csv"))
                .WithSummary("Картки з фільтром у CSV (UTF-8 з BOM, крапка з комою)");
            group.MapMethods("/cards.xlsx", [HttpMethods.Get, HttpMethods.Head], ExportAsync("xlsx"))
                .WithSummary("Картки з фільтром в Excel");
            // ті самі вивантаження під /export для посилань зі сторінок; в описі API лишаємо одну копію
            if (prefix == "/export") group.ExcludeFromDescription();
        }
    }

    private static Results<Ok<CardsPage>, ValidationProblem> Cards(HttpRequest request, EditionStore store)
    {
        var edition = store.Require();
        var errors = CardFilter.TryParse(request.Query, edition, allowPageSize: true, out var filter);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors, title: "Неправильні параметри фільтра");

        var found = filter.Apply(edition);
        var items = found
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(i => ApiMapping.Card(edition, edition.Edition.Cards[i]))
            .ToList();
        return TypedResults.Ok(new CardsPage(found.Count, filter.Page, filter.PageSize, edition.Edition.Generated, items));
    }

    private static IResult Card(string code, EditionStore store)
    {
        var edition = store.Require();
        return edition.CardIndexByCode.TryGetValue(code.ToUpperInvariant(), out int index)
            ? TypedResults.Ok(ApiMapping.Card(edition, edition.Edition.Cards[index]))
            : ApiMapping.NotFound("Серед відкритих карток поточного випуску такої немає");
    }

    private static Func<HttpContext, EditionStore, ExportFiles, SharedLimits, CancellationToken, Task<IResult>> ExportAsync(string format) =>
        async (context, store, files, limits, ct) =>
        {
            var edition = store.Require();
            var errors = CardFilter.TryParseForExport(context.Request.Query, edition, out var filter);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors, title: "Неправильні параметри фільтра");

            if (filter.IsEmpty) return Prepared(context, await files.GetAsync(edition, ct), format);

            string name = ExportRows.FileName(edition, format);
            string type = format == "csv" ? CsvExport.ContentType : XlsxExport.ContentType;
            if (HttpMethods.IsHead(context.Request.Method)) return new Attachment(type, name, null);

            // збираємо в тимчасовий файл і віддаємо вже після збірки: повільний клієнт не тримає чергу збірок
            var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            bool built = false;
            try
            {
                using var permit = await limits.ExportBuildAsync(ct);
                if (!permit.Granted) return TooManyBuilds(context, permit.RetryAfter);

                var rows = ExportRows.Rows(edition, filter.Apply(edition));
                var headers = ExportRows.Headers(edition);
                if (format == "csv") await CsvExport.WriteAsync(buffer, headers, rows, ct);
                else await XlsxExport.WriteAsync(buffer, headers, rows, ct);
                built = true;
            }
            finally
            {
                if (!built) await buffer.DisposeAsync();
            }
            buffer.Position = 0;
            return new Attachment(type, name, buffer);
        };

    private static IResult Prepared(HttpContext context, ExportFileSet set, string format)
    {
        var file = set.Xlsx;
        if (format == "csv")
        {
            bool gzip = AcceptsGzip(context.Request);
            file = gzip ? set.CsvGzip : set.Csv;
            context.Response.Headers.Vary = HeaderNames.AcceptEncoding;
            // уже стиснене: з цим заголовком стиснення відповідей його вдруге не стискає
            if (gzip) context.Response.Headers.ContentEncoding = "gzip";
        }
        return TypedResults.PhysicalFile(file.Path, file.ContentType, file.DownloadName, file.LastModified, file.ETag, enableRangeProcessing: true);
    }

    private static bool AcceptsGzip(HttpRequest request) =>
        request.GetTypedHeaders().AcceptEncoding.Any(e =>
            e.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase) && (e.Quality ?? 1) > 0);

    private static IResult TooManyBuilds(HttpContext context, TimeSpan retryAfter)
    {
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Забагато вивантажень",
            detail: "Сервіс саме збирає інші файли. Спробуйте пізніше; скільки чекати, сказано в заголовку Retry-After. Повний файл без фільтра віддається без черги.");
    }

    /// <summary>Файл як вкладення; без тіла для HEAD.</summary>
    private sealed class Attachment(string contentType, string fileName, Stream? body) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            var response = context.Response;
            response.ContentType = contentType;
            response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            if (body is null) return;
            await using (body)
            {
                response.ContentLength = body.Length;
                await body.CopyToAsync(response.Body, context.RequestAborted);
            }
        }
    }
}
