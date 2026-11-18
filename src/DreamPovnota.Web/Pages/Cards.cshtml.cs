using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class CardsModel(EditionStore store) : FormPageModel
{
    public LoadedEdition Edition { get; private set; } = null!;

    public CardFilter Filter { get; private set; } = CardFilter.Empty;

    public IReadOnlyList<int> Found { get; private set; } = [];

    public IReadOnlyList<int> PageCards { get; private set; } = [];

    public int Pages { get; private set; }

    public string? OrganisationTitle { get; private set; }

    public void OnGet()
    {
        Edition = store.Require();
        Errors = CardFilter.TryParse(Request.Query, Edition, allowPageSize: false, out var filter);
        if (Errors.Count > 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            // неправильне значення не застосовуємо, решту фільтра лишаємо
            filter = filter with
            {
                Region = Errors.ContainsKey("region") ? null : filter.Region,
                Organisation = Errors.ContainsKey("org") ? null : filter.Organisation,
                Stage = Errors.ContainsKey("stage") ? null : filter.Stage,
                Sector = Errors.ContainsKey("sector") ? null : filter.Sector,
                Missing = Errors.ContainsKey("missing") ? null : filter.Missing,
                Query = Errors.ContainsKey("q") ? null : filter.Query,
            };
            MoveUnknownParameters();
        }

        Filter = filter;
        if (filter.Organisation is { } key && Edition.OrganisationsByKey.TryGetValue(key, out var organisation))
            OrganisationTitle = organisation.Title;

        Found = filter.Apply(Edition);
        Pages = Math.Max(1, (Found.Count + filter.PageSize - 1) / filter.PageSize);
        PageCards = Found.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
    }

    // помилки параметрів, для яких на формі немає поля, показуємо внизу форми
    private void MoveUnknownParameters()
    {
        string[] fields = ["q", "region", "org", "stage", "sector", "missing", "seeks", "page"];
        var other = Errors.Where(kv => !fields.Contains(kv.Key)).Select(kv => $"{kv.Key}: {kv.Value[0]}").ToList();
        if (Errors.TryGetValue("org", out var org)) other.Add(org[0]);
        if (other.Count > 0) Errors["other"] = [string.Join("; ", other)];
    }
}
