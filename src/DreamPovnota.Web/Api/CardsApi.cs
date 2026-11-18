using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Http.HttpResults;

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

    public static void MapExports(this IEndpointRouteBuilder app)
    {
        foreach (string prefix in new[] { "/export", "/api/v1" })
        {
            var group = app.MapGroup(prefix).WithTags("Вивантаження").CacheOutput(ImportService.CacheTag);
            group.MapGet("/cards.csv", (HttpContext context, EditionStore store, CancellationToken ct) =>
                    ExportAsync(context, store, "csv", ct))
                .WithSummary("Картки з фільтром у CSV (UTF-8 з BOM, крапка з комою)");
            group.MapGet("/cards.xlsx", (HttpContext context, EditionStore store, CancellationToken ct) =>
                    ExportAsync(context, store, "xlsx", ct))
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

    private static async Task ExportAsync(HttpContext context, EditionStore store, string format, CancellationToken ct)
    {
        var edition = store.Require();
        var errors = CardFilter.TryParse(context.Request.Query, edition, allowPageSize: false, out var filter);
        if (errors.Count > 0)
        {
            await TypedResults.ValidationProblem(errors, title: "Неправильні параметри фільтра").ExecuteAsync(context);
            return;
        }

        var rows = ExportRows.Rows(edition, filter.Apply(edition));
        var headers = ExportRows.Headers(edition);
        var response = context.Response;
        response.Headers.ContentDisposition = $"attachment; filename=\"{ExportRows.FileName(edition, format)}\"";
        if (format == "csv")
        {
            response.ContentType = CsvExport.ContentType;
            await CsvExport.WriteAsync(response.Body, headers, rows, ct);
        }
        else
        {
            response.ContentType = XlsxExport.ContentType;
            await XlsxExport.WriteAsync(response.Body, headers, rows, ct);
        }
    }
}
