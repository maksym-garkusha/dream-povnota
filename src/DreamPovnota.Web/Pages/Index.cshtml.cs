using DreamPovnota.Web.Import;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DreamPovnota.Web.Pages;

public sealed class IndexModel(EditionStore store) : PageModel
{
    public LoadedEdition Edition { get; private set; } = null!;

    public void OnGet() => Edition = store.Require();
}
