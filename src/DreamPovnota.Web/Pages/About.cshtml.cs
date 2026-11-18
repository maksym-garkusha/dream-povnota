using DreamPovnota.Web.Import;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class AboutModel(EditionStore store) : PageModel
{
    public LoadedEdition Edition { get; private set; } = null!;

    public void OnGet() => Edition = store.Require();
}
