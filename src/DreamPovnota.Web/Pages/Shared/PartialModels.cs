using DreamPovnota.Stats;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Pages.Shared;

public sealed record CardTableModel(LoadedEdition Edition, IReadOnlyList<int> Cards, bool ShowOrganisation = true);

/// <param name="Filter">Параметри /cards, до яких посилання «без цього поля» додає missing.</param>
public sealed record FillTableModel(LoadedEdition Edition, IReadOnlyList<CheckTally> Tallies, IReadOnlyDictionary<string, string> Filter);

public sealed record BarModel(double Share);

/// <param name="Services">Числа агентства й служб відновлення разом замість усіх карток.</param>
/// <param name="Caption">Прихований підпис: таблиць на сторінці три з однаковими колонками, і на слух їх треба розрізняти.</param>
public sealed record ChangesTableModel(IReadOnlyList<Changes.PeriodChanges> Periods, Changes.PeriodKind Kind, bool Services, string Caption);

/// <param name="Prefix">Початок id графіків, щоб вони не повторювались на сторінці.</param>
public sealed record ChangeChartsModel(
    IReadOnlyList<Changes.PeriodChanges> Periods,
    Changes.PeriodKind Kind,
    IReadOnlyList<ChangeMeasure> Measures,
    Func<Changes.PeriodChanges, Changes.ChangeCounts> Slice,
    string Prefix);
