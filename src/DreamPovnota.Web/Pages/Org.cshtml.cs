using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class OrgModel(EditionStore store) : PageModel
{
    public SliceView View { get; private set; } = null!;

    public OrganisationEntry Organisation { get; private set; } = null!;

    public IReadOnlyList<int> PageCards { get; private set; } = [];

    public IActionResult OnGet(string? key)
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
        return Page();
    }
}
