using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

public sealed record RegionOrganisation(OrganisationEntry Organisation, int Cards);

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class RegionModel(EditionStore store, HistoryReader history) : PageModel
{
    public string Key { get; private set; } = "";

    public SliceView View { get; private set; } = null!;

    public IReadOnlyList<RegionOrganisation> Organisations { get; private set; } = [];

    public IReadOnlyList<int> PageCards { get; private set; } = [];

    public HistoryView History { get; private set; } = null!;

    public string FeedPath { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string key, CancellationToken ct)
    {
        var edition = store.Require();
        if (!edition.RegionIndex.TryGetValue(key, out int index)) return NotFound();

        Key = key;
        var cards = edition.CardsByRegion[key];
        View = new SliceView(
            edition,
            edition.Edition.Regions[index].Title,
            cards,
            new Dictionary<string, string> { ["region"] = key },
            "/api/v1/regions/" + key);

        var perOrganisation = cards
            .GroupBy(i => edition.OrganisationOf(edition.Edition.Cards[i]).Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Organisations = edition.OrganisationsAmong(cards)
            .Select(o => new RegionOrganisation(o, perOrganisation[o.Key]))
            .ToList();
        PageCards = cards.Take(CardFilter.PageSizeOnPages).ToList();
        FeedPath = "/feeds/region/" + key + ".atom";
        History = await HistoryView.LoadAsync(
            HttpContext, history, edition, new HistoryScope(Region: key),
            token => history.SliceTrendAsync('r', key, token), FeedPath, ct);
        return Page();
    }
}
