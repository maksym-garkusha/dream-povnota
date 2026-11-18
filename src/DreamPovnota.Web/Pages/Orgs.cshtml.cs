using System.Globalization;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class OrgsModel(EditionStore store) : FormPageModel
{
    public const int PageSize = 50;

    public string? Query { get; private set; }

    public IReadOnlyList<OrganisationEntry> Found { get; private set; } = [];

    public IReadOnlyList<OrganisationEntry> PageItems { get; private set; } = [];

    public int PageNumber { get; private set; } = 1;

    public int Pages { get; private set; } = 1;

    // "page" у Razor Pages зайнятий маршрутом сторінки, тому параметри читаємо з запиту напряму
    public IActionResult OnGet()
    {
        string? q = Request.Query["q"].Count == 1 ? Request.Query["q"][0] : null;
        string? page = Request.Query["page"].Count == 1 ? Request.Query["page"][0] : null;
        var edition = store.Require();
        Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        if (Query is { Length: > CardFilter.MaxQueryLength })
            Errors["q"] = [$"Не довше за {CardFilter.MaxQueryLength} знаків"];
        else if (Query is { Length: 1 })
            Errors["q"] = ["Щонайменше 2 знаки"];
        if (page is not null && !(int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                                  && number is >= 1 and <= CardFilter.MaxPage))
            Errors["page"] = [$"Номер сторінки від 1 до {CardFilter.MaxPage}"];
        else if (page is not null)
            PageNumber = int.Parse(page, CultureInfo.InvariantCulture);

        if (Errors.Count > 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            if (Errors.ContainsKey("q")) Query = null;
        }

        Found = OrganisationSearch.Find(edition, Query);
        if (Errors.Count == 0 && Query is { Length: 8 } && Found.Count == 1 && Query == Found[0].Key)
            return Redirect(Web.Format.OrganisationPath(Found[0].Key));

        Pages = Math.Max(1, (Found.Count + PageSize - 1) / PageSize);
        PageItems = Found.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        return Page();
    }

    public string Link(int page)
    {
        var values = new List<KeyValuePair<string, string?>>();
        if (Query is not null) values.Add(new("q", Query));
        if (page > 1) values.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        return "/orgs" + QueryString.Create(values).Value;
    }
}
