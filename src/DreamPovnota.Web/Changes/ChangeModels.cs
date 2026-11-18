namespace DreamPovnota.Web.Changes;

/// <summary>Скільки разів за період дозаповнили поле одного типу.</summary>
public sealed record FieldCount(string Check, string Title, int Fields);

/// <summary>Скільки карток перейшло з однієї стадії на іншу.</summary>
public sealed record StageMove(string From, string FromTitle, string To, string ToTitle, int Cards);

/// <summary>Організація з переліку служб, де за період дозаповнили найбільше полів.</summary>
public sealed record ServiceExample(string Edrpou, string Title, int Fields, int Cards);

/// <summary>Рух у картках за період: стан на початок порівняно зі станом на кінець.</summary>
/// <param name="UpdatedCards">Картки, у яких змінилась дата оновлення в DREAM.</param>
/// <param name="FieldsFilled">Поля, що на початок були доречні й порожні, а на кінець заповнені.</param>
/// <param name="CardsWithFields">У скількох картках дозаповнено хоча б одне поле.</param>
/// <param name="NewCards">Картки, яких до початку періоду в історії не було.</param>
/// <param name="StageMoves">Картки, що на кінець періоду на іншій стадії, ніж на початок.</param>
public sealed record ChangeCounts(
    int UpdatedCards,
    int FieldsFilled,
    int CardsWithFields,
    int NewCards,
    int StageMoves,
    IReadOnlyList<FieldCount> Fields,
    IReadOnlyList<StageMove> Stages)
{
    public static ChangeCounts Empty { get; } = new(0, 0, 0, 0, 0, [], []);

    public bool HasChanges => UpdatedCards > 0 || FieldsFilled > 0 || NewCards > 0 || StageMoves > 0;
}

/// <summary>Тиждень, день або весь показаний час.</summary>
/// <param name="IsoWeek">«2026-W47» для тижня, null для дня й усього часу.</param>
/// <param name="ObservedFrom">З якого дня в періоді є дані: пізніше за From, якщо історія почалась усередині.</param>
/// <param name="InProgress">Період ще не скінчився.</param>
/// <param name="Snapshots">Скільки знімків випущено в період.</param>
/// <param name="ComparedFrom">Дата випуску знімка на початок; null, якщо порівнювати не було з чим.</param>
/// <param name="RulesChanged">Між знімками змінився перелік перевірок: поля порівнюються лише ті, що є в обох.</param>
/// <param name="Services">Те саме для агентства й служб відновлення разом.</param>
/// <param name="Fastest">До трьох служб, де дозаповнили найбільше полів; порожньо, якщо ніде.</param>
public sealed record PeriodChanges(
    DateOnly From,
    DateOnly To,
    string? IsoWeek,
    DateOnly ObservedFrom,
    bool InProgress,
    int Snapshots,
    DateTimeOffset? ComparedFrom,
    DateTimeOffset? ComparedTo,
    bool RulesChanged,
    ChangeCounts All,
    ChangeCounts Services,
    IReadOnlyList<ServiceExample> Fastest);

/// <summary>Рух у відкритих картках за тижнями з історії сервісу.</summary>
/// <param name="HistoryFrom">Дата випуску першого знімка; null, поки знімків немає.</param>
/// <param name="Total">Увесь показаний час: від початку першого тижня до останнього знімка.</param>
/// <param name="Weeks">Тижні від найстаршого, не більше <see cref="ChangeCalendar.MaxWeeks"/>.</param>
/// <param name="FirstWeekDays">Дні першого тижня історії, поки він серед показаних тижнів.</param>
public sealed record ChangeSummary(
    DateTimeOffset? HistoryFrom,
    DateTimeOffset? LastSnapshot,
    int Snapshots,
    PeriodChanges? Total,
    IReadOnlyList<PeriodChanges> Weeks,
    IReadOnlyList<PeriodChanges> FirstWeekDays);
