using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class DigestModel(EditionStore store, DigestFiles files) : PageModel
{
    public DigestSet Set { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Set = await files.GetAsync(store.Require(), ct);
}
