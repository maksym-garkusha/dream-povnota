using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class OrgModel(EditionStore store, HistoryReader history) : PageModel
{
    public SliceView View { get; private set; } = null!;

    public OrganisationEntry Organisation { get; private set; } = null!;

    public IReadOnlyList<int> PageCards { get; private set; } = [];

    public HistoryView History { get; private set; } = null!;

    public string FeedPath { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string? key, CancellationToken ct)
    {
        var edition = store.Require();
        if (key is null || key.Length > CardFilter.MaxKeyLength) return NotFound();
        // групи підприємців і «не вказано» не є організаціями, тож їх у словнику немає
        if (!edition.OrganisationsByKey.TryGetValue(OrganisationSearch.NormaliseKey(key), out var organisation)) return NotFound();

        Organisation = organisation;
        View = new SliceView(
            edition,
            organisation.Title,
            edition.CardsByOrganisation[organisation.Key],
            new Dictionary<string, string> { ["org"] = organisation.Key },
            "/api/v1/orgs/" + Uri.EscapeDataString(organisation.Key));
        PageCards = View.Cards.Take(CardFilter.PageSizeOnPages).ToList();
        FeedPath = "/feeds/org/" + Uri.EscapeDataString(organisation.Key) + ".atom";
        History = await HistoryView.LoadAsync(
            HttpContext, history, edition, new HistoryScope(Organisation: organisation.Key),
            token => history.OrganisationTrendAsync(organisation.Key, token), FeedPath, ct);
        return Page();
    }
}
