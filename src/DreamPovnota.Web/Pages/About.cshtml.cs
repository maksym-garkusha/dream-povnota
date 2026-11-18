using DreamPovnota.Export;
using DreamPovnota.Web.Import;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class AboutModel(EditionStore store) : PageModel
{
    public LoadedEdition Edition { get; private set; } = null!;

    public void OnGet() => Edition = store.Require();

    /// <summary>«усі стадії», «тільки «Реалізація»», «з «Визначено»»: так само, як на статичній сторінці.</summary>
    public string StageRange(EditionCheck check)
    {
        var stages = Edition.Edition.Stages;
        string Title(string key) => stages.FirstOrDefault(s => s.Key == key)?.Title ?? key;

        string range = check.From == stages[0].Key && check.Until == stages[^1].Key ? "усі стадії"
            : check.From == check.Until ? $"тільки «{Title(check.From)}»"
            : check.Until == stages[^1].Key ? $"з «{Title(check.From)}»"
            : $"з «{Title(check.From)}» до «{Title(check.Until)}»";
        return check.Condition is { } condition ? $"{range}, {condition}" : range;
    }
}
