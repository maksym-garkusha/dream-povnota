using DreamPovnota.Web.Changes;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Web;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Pages;

/// <summary>Що рахує один графік: назва, одиниця осі й число з підсумків періоду.</summary>
public sealed record ChangeMeasure(string Key, string Title, string Unit, Func<ChangeCounts, int> Value);

[OutputCache(PolicyName = ImportService.CacheTag)]
public sealed class ChangesModel(EditionStore store, HistoryReader history, TimeProvider clock) : PageModel
{
    public static IReadOnlyList<ChangeMeasure> Measures { get; } =
    [
        new("updated", "Оновлено карток", "карток", c => c.UpdatedCards),
        new("fields", "Дозаповнено полів", "полів", c => c.FieldsFilled),
        new("new", "Нових карток", "карток", c => c.NewCards),
        new("stage", "Перейшли на іншу стадію", "карток", c => c.StageMoves),
    ];

    /// <summary>Null, коли історія недоступна.</summary>
    public ChangeSummary? Summary { get; private set; }

    public bool Available => Summary is not null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Summary = await ChangeSummaryBuilder.LoadAsync(history, ChangeSummaryBuilder.StagesOf(store.Require()), clock.GetUtcNow(), ct);
        // пояснення про недоступну чи порожню історію не має висіти в кеші, коли знімок уже є
        if (Summary is null or { Snapshots: 0 } && HttpContext.Features.Get<IOutputCacheFeature>() is { } cache)
            cache.Context.AllowCacheStorage = false;
    }

    /// <summary>Підпис під числом: «1 оновлена картка», «12 дозаповнених полів».</summary>
    public static string FigureLabel(ChangeMeasure measure, int count) => measure.Key switch
    {
        "updated" => Format.Plural(count, "оновлена картка", "оновлені картки", "оновлених карток"),
        "fields" => Format.Plural(count, "дозаповнене поле", "дозаповнені поля", "дозаповнених полів"),
        "new" => Format.Plural(count, "нова картка", "нові картки", "нових карток"),
        _ => Format.Plural(count, "перейшла на іншу стадію", "перейшли на іншу стадію", "перейшли на іншу стадію"),
    };

    public static string Chart(string id, ChangeMeasure measure, IReadOnlyList<PeriodChanges> periods, PeriodKind kind, Func<PeriodChanges, ChangeCounts> slice) =>
        ColumnChart.Render(
            id,
            measure.Title + (kind == PeriodKind.Day ? " за днями" : " за тижнями"),
            measure.Unit,
            periods.Select(p =>
            {
                int? value = p.ComparedFrom is null ? null : measure.Value(slice(p));
                string text = value is { } v ? Format.Number(v) : "порівнювати не було з чим";
                return new ChartColumn(ChangeText.Short(p), value, ChangeText.IsPartial(p), $"{ChangeText.Title(p, kind)}: {text}");
            }).ToList());

    /// <summary>Число в таблиці; риска, якщо за період порівнювати не було з чим.</summary>
    public static string Cell(PeriodChanges period, int value) => period.ComparedFrom is null ? "–" : Format.Number(value);
}
