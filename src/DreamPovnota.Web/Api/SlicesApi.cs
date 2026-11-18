using System.Globalization;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DreamPovnota.Web.Api;

public static class SlicesApi
{
    public const int OrganisationsPageSize = 100;

    public static void MapSlicesApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").CacheOutput(ImportService.CacheTag);

        api.MapGet("/orgs", Organisations).WithTags("Організації")
            .WithSummary("Пошук організацій за назвою (від 2 знаків) або ЄДРПОУ");
        api.MapGet("/orgs/{key}", Organisation).WithTags("Організації")
            .WithSummary("Розріз за організацією: ключ це ЄДРПОУ або n: і назва");
        api.MapGet("/regions", (EditionStore store) =>
            {
                var edition = store.Require();
                return TypedResults.Ok(edition.Edition.Regions.Select(r => ApiMapping.Slice(r, edition.Edition.Checks)).ToList());
            })
            .WithTags("Області").WithSummary("Розрізи за областями");
        api.MapGet("/regions/{key:regex(^(\\d{{2}}|multi|none)$)}", Region).WithTags("Області")
            .WithSummary("Розріз за однією областю");
    }

    private static Results<Ok<OrganisationsPage>, ValidationProblem> Organisations(string? q, string? page, EditionStore store)
    {
        var edition = store.Require();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? query = q?.Trim();
        if (query is { Length: > Lists.CardFilter.MaxQueryLength })
            errors["q"] = [$"Не довше за {Lists.CardFilter.MaxQueryLength} знаків"];
        else if (query is { Length: 1 })
            errors["q"] = ["Щонайменше 2 знаки"];

        int pageNumber = 1;
        if (page is not null && !(int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out pageNumber)
                                  && pageNumber is >= 1 and <= Lists.CardFilter.MaxPage))
            errors["page"] = [$"Номер сторінки від 1 до {Lists.CardFilter.MaxPage}"];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors, title: "Неправильні параметри пошуку");

        var found = OrganisationSearch.Find(edition, query);
        var items = found
            .Skip((pageNumber - 1) * OrganisationsPageSize)
            .Take(OrganisationsPageSize)
            .Select(o => new OrganisationItem(o.Key, o.Edrpou, o.Title, o.Open, Math.Round(o.AverageScore, 6)))
            .ToList();
        return TypedResults.Ok(new OrganisationsPage(found.Count, pageNumber, OrganisationsPageSize, items));
    }

    private static IResult Organisation(string key, EditionStore store)
    {
        var edition = store.Require();
        return edition.OrganisationsByKey.TryGetValue(OrganisationSearch.NormaliseKey(key), out var organisation)
            ? TypedResults.Ok(ApiMapping.Slice(edition, organisation))
            : ApiMapping.NotFound("Організації з таким ключем серед відкритих карток немає");
    }

    private static IResult Region(string key, EditionStore store)
    {
        var edition = store.Require();
        return edition.RegionIndex.TryGetValue(key, out int index)
            ? TypedResults.Ok(ApiMapping.Slice(edition.Edition.Regions[index], edition.Edition.Checks))
            : ApiMapping.NotFound("У поточному випуску карток цієї області немає");
    }
}
