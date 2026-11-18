using DreamPovnota.Digest;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class DigestOrgModel(EditionStore store, DigestFiles files) : PageModel
{
    public OrganisationDigest Digest { get; private set; } = null!;

    /// <summary>Чи є в організації відкриті картки, тобто сторінка /org/{код}.</summary>
    public bool HasOrganisationPage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken ct)
    {
        if (DigestOrganisations.Find(code) is null) return NotFound();
        var edition = store.Require();
        Digest = (await files.GetAsync(edition, ct)).Find(code)!.Digest;
        HasOrganisationPage = edition.OrganisationsByKey.ContainsKey(code);
        return Page();
    }
}
