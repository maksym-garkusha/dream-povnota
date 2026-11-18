using DreamPovnota.Stats;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Pages.Shared;

public sealed record CardTableModel(LoadedEdition Edition, IReadOnlyList<int> Cards, bool ShowOrganisation = true);

/// <param name="Filter">Параметри /cards, до яких посилання «без цього поля» додає missing.</param>
public sealed record FillTableModel(LoadedEdition Edition, IReadOnlyList<CheckTally> Tallies, IReadOnlyDictionary<string, string> Filter);

public sealed record BarModel(double Share);

/// <param name="Services">Числа агентства й служб відновлення замість усіх карток, з «де пішло найшвидше».</param>
public sealed record ChangesTableModel(IReadOnlyList<Changes.PeriodChanges> Periods, Changes.PeriodKind Kind, bool Services);
