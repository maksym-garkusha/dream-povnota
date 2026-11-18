namespace DreamPovnota.Web.History;

/// <param name="First">Дата випуску першого знімка: з нього почалась історія.</param>
public sealed record HistoryStatus(int Snapshots, DateTimeOffset? First, int? LastId, DateTimeOffset? LastGenerated);

/// <param name="Generated">Дата випуску останнього знімка; null, поки знімків немає.</param>
public sealed record LastSnapshot(DateTimeOffset? Generated);

public sealed record SnapshotInfo(int Id, DateTimeOffset Generated, DateTimeOffset? Synced, int Open, int Added, int Removed, int Changed, bool RulesChanged);

public enum ChangeKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>Що сталося з карткою у знімку порівняно з попереднім.</summary>
/// <param name="NowFilled">Коди перевірок, заповнених тепер, а раніше ні.</param>
/// <param name="NoLongerFilled">Коди перевірок, заповнених раніше, а тепер ні.</param>
/// <param name="NewlyApplicable">Коди перевірок, що стали доречними (наприклад, після зміни стадії).</param>
public sealed record CardChange(
    int Snapshot,
    string Code,
    string Title,
    ChangeKind Kind,
    string? StageFrom,
    string? StageTo,
    IReadOnlyList<string> NowFilled,
    IReadOnlyList<string> NoLongerFilled,
    IReadOnlyList<string> NewlyApplicable);

public sealed record TrendPoint(int Snapshot, DateTimeOffset Date, int Open, int Complete, double AvgScore);

public sealed record CardHistoryPoint(int Snapshot, DateTimeOffset Date, string Stage, int Filled, int Applicable);

/// <summary>Чиї зміни показувати: організації (ключ як на сторінці), області або всі.</summary>
public sealed record HistoryScope(string? Organisation = null, string? Region = null);

public sealed record RuleCheck(string Code, string Title);

/// <summary>Знімок з переліком перевірок, за яким у ньому складено бітові маски.</summary>
/// <param name="RulesChanged">Перелік перевірок інший, ніж у попередньому знімку.</param>
public sealed record SnapshotMark(int Id, DateTimeOffset Generated, bool RulesChanged, IReadOnlyList<RuleCheck> Checks);

/// <summary>Два знімки, стан карток у яких порівнюється: на початок періоду і на кінець.</summary>
/// <param name="Period">Номер періоду, яким позначено рядки відповіді.</param>
public sealed record SnapshotPair(int Period, int From, int To);

/// <summary>
/// Картка, у якої між двома знімками пари щось змінилось і яка відкрита в другому.
/// Поля «From» порожні, якщо в першому знімку картки серед відкритих не було.
/// </summary>
/// <param name="Appeared">Картка вперше з'явилась в історії після першого знімка пари.</param>
/// <param name="Edrpou">ЄДРПОУ організації, що веде картку в другому знімку.</param>
public sealed record CardTransition(
    int Period,
    bool Appeared,
    string? Edrpou,
    string? StageFrom,
    DateOnly? UpdatedFrom,
    int? ApplicableFrom,
    int? FilledFrom,
    string StageTo,
    DateOnly UpdatedTo,
    int FilledTo);
