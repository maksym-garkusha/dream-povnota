using DreamPovnota.Digest;
using DreamPovnota.Web.History;

namespace DreamPovnota.Web.Changes;

public enum PeriodKind
{
    Total,
    Week,
    Day,
}

/// <summary>Період сторінки змін і знімки на його краях.</summary>
/// <param name="Number">Номер, яким позначено рядки відповіді бази для цього періоду.</param>
public sealed record PlannedPeriod(int Number, PeriodKind Kind, PeriodSpan Span, SnapshotMark? From, SnapshotMark? To, int Snapshots)
{
    public bool Compares => From is not null && To is not null;
}

/// <summary>
/// Рахує рух у картках за періодами з історії: які картки оновили, які поля дозаповнили,
/// скільки карток нових і скільки перейшло на іншу стадію. Окремо те саме для агентства
/// й служб відновлення з <see cref="DigestOrganisations"/>, разом: служби не називаються
/// і між собою не порівнюються, є тільки число тих, де щось змінилось.
/// </summary>
public static class ChangeSummaryBuilder
{
    // «Оновлювалась за останній рік» не поле картки, а наслідок дати оновлення,
    // яку вже пораховано в оновлених картках
    private static readonly HashSet<string> NotFields = new(StringComparer.Ordinal) { "fresh" };

    private static readonly HashSet<string> Services = DigestOrganisations.All.Select(o => o.Edrpou).ToHashSet(StringComparer.Ordinal);

    /// <summary>Стадії поточного випуску в його порядку: ключ і назва для переходів між стадіями.</summary>
    public static IReadOnlyList<(string Key, string Title)> StagesOf(Import.LoadedEdition edition) =>
        edition.Edition.Stages.Select(s => (s.Key, s.Title)).ToList();

    public static async Task<ChangeSummary?> LoadAsync(
        HistoryReader history, IReadOnlyList<(string Key, string Title)> stages, DateTimeOffset now, CancellationToken ct)
    {
        var snapshots = await history.SnapshotMarksAsync(ct);
        if (snapshots is null) return null;
        var plan = Plan(snapshots);
        var rows = await history.TransitionsAsync(Pairs(plan), ct);
        return rows is null ? null : Build(snapshots, plan, rows, stages, now);
    }

    /// <summary>Увесь показаний час, тижні від найстаршого і дні першого тижня, якщо він серед показаних.</summary>
    /// <param name="snapshots">Усі знімки за зростанням номера.</param>
    public static IReadOnlyList<PlannedPeriod> Plan(IReadOnlyList<SnapshotMark> snapshots)
    {
        if (snapshots.Count == 0) return [];
        var first = snapshots[0].Generated;
        var last = snapshots[^1].Generated;

        var weeks = ChangeCalendar.Weeks(first, last);
        var spans = new List<(PeriodKind, PeriodSpan)>
        {
            (PeriodKind.Total, ChangeCalendar.Span(weeks[0].From, ChangeCalendar.KyivDate(last))),
        };
        spans.AddRange(weeks.Select(w => (PeriodKind.Week, w)));
        if (weeks[0].From == ChangeCalendar.Monday(ChangeCalendar.KyivDate(first)))
            spans.AddRange(ChangeCalendar.FirstWeekDays(first, last).Select(d => (PeriodKind.Day, d)));

        return spans.Select((p, number) =>
        {
            var edges = ChangeCalendar.Edges(snapshots, p.Item2);
            return new PlannedPeriod(number, p.Item1, p.Item2, edges?.From, edges?.To, ChangeCalendar.SnapshotsIn(snapshots, p.Item2));
        }).ToList();
    }

    public static IReadOnlyList<SnapshotPair> Pairs(IReadOnlyList<PlannedPeriod> plan) =>
        plan.Where(p => p.Compares).Select(p => new SnapshotPair(p.Number, p.From!.Id, p.To!.Id)).ToList();

    public static ChangeSummary Build(
        IReadOnlyList<SnapshotMark> snapshots, IReadOnlyList<PlannedPeriod> plan, IReadOnlyList<CardTransition> rows,
        IReadOnlyList<(string Key, string Title)> stages, DateTimeOffset now)
    {
        if (snapshots.Count == 0) return new ChangeSummary(null, null, 0, null, [], []);

        var byPeriod = rows.ToLookup(r => r.Period);
        var periods = plan.Select(p => (p.Kind, Changes: Period(p, byPeriod[p.Number], snapshots, stages, now))).ToList();
        return new ChangeSummary(
            snapshots[0].Generated,
            snapshots[^1].Generated,
            snapshots.Count,
            periods.Single(p => p.Kind == PeriodKind.Total).Changes,
            periods.Where(p => p.Kind == PeriodKind.Week).Select(p => p.Changes).ToList(),
            periods.Where(p => p.Kind == PeriodKind.Day).Select(p => p.Changes).ToList());
    }

    private static PeriodChanges Period(
        PlannedPeriod period, IEnumerable<CardTransition> rows, IReadOnlyList<SnapshotMark> snapshots,
        IReadOnlyList<(string Key, string Title)> stages, DateTimeOffset now)
    {
        var span = period.Span;
        var observedFrom = snapshots[0].Generated > span.Start ? ChangeCalendar.KyivDate(snapshots[0].Generated) : span.From;
        string? isoWeek = period.Kind == PeriodKind.Week ? ChangeCalendar.IsoWeek(span.From) : null;
        bool inProgress = now < span.End;

        if (!period.Compares)
            return new PeriodChanges(span.From, span.To, isoWeek, observedFrom, inProgress, period.Snapshots,
                null, null, false, ChangeCounts.Empty, ChangeCounts.Empty, 0);

        var from = period.From!;
        var to = period.To!;
        var fields = FieldMap(from, to);
        var all = new Tally();
        var services = new Tally();
        var changedServices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var filled = Filled(row, fields);
            all.Add(row, filled);
            if (row.Edrpou is not { } edrpou || !Services.Contains(edrpou)) continue;
            if (services.Add(row, filled)) changedServices.Add(edrpou);
        }

        bool rulesChanged = snapshots.Any(s => s.Id > from.Id && s.Id <= to.Id && s.RulesChanged);

        return new PeriodChanges(span.From, span.To, isoWeek, observedFrom, inProgress, period.Snapshots,
            from.Generated, to.Generated, rulesChanged, all.Counts(to, stages), services.Counts(to, stages), changedServices.Count);
    }

    /// <summary>Перевірки, що є в обох знімках, з бітом у кожному: біти різних переліків означають різне.</summary>
    private static List<(string Code, int FromBit, int ToBit)> FieldMap(SnapshotMark from, SnapshotMark to)
    {
        var fromBits = from.Checks.Select((c, i) => (c.Code, i)).ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        return to.Checks
            .Select((c, i) => (c.Code, ToBit: i))
            .Where(c => !NotFields.Contains(c.Code) && fromBits.ContainsKey(c.Code))
            .Select(c => (c.Code, fromBits[c.Code], c.ToBit))
            .ToList();
    }

    /// <summary>Поля, доречні й порожні на початок і заповнені на кінець. Поле, яке на початок було недоречним, сюди не входить: тоді не відомо, чи воно було порожнім.</summary>
    private static List<string> Filled(CardTransition row, List<(string Code, int FromBit, int ToBit)> fields)
    {
        if (row.Appeared || row.ApplicableFrom is not { } applicable || row.FilledFrom is not { } before) return [];
        return fields
            .Where(f => Has(applicable, f.FromBit) && !Has(before, f.FromBit) && Has(row.FilledTo, f.ToBit))
            .Select(f => f.Code)
            .ToList();
    }

    private static bool Has(int mask, int bit) => (mask & (1 << bit)) != 0;

    private sealed class Tally
    {
        private readonly Dictionary<string, int> fields = new(StringComparer.Ordinal);
        private readonly Dictionary<(string From, string To), int> stages = [];
        private int updated, filled, cardsWithFields, added, moved;

        /// <returns>Чи порахувалась картка хоч в одному з чисел.</returns>
        public bool Add(CardTransition row, IReadOnlyList<string> filledCodes)
        {
            if (row.Appeared)
            {
                added++;
                return true;
            }
            // картка повернулась до відкритих після того, як зникла: не нова, а порівнювати нема з чим
            if (row.StageFrom is null) return false;

            bool counted = false;
            if (row.UpdatedFrom != row.UpdatedTo)
            {
                updated++;
                counted = true;
            }
            if (row.StageFrom != row.StageTo)
            {
                moved++;
                stages[(row.StageFrom, row.StageTo)] = stages.GetValueOrDefault((row.StageFrom, row.StageTo)) + 1;
                counted = true;
            }
            if (filledCodes.Count == 0) return counted;
            cardsWithFields++;
            filled += filledCodes.Count;
            foreach (string code in filledCodes) fields[code] = fields.GetValueOrDefault(code) + 1;
            return true;
        }

        public ChangeCounts Counts(SnapshotMark to, IReadOnlyList<(string Key, string Title)> stageList)
        {
            var order = to.Checks.Select((c, i) => (c, i)).ToDictionary(x => x.c.Code, x => x.i, StringComparer.Ordinal);
            var titles = to.Checks.ToDictionary(c => c.Code, c => c.Title, StringComparer.Ordinal);
            var stageOrder = stageList.Select((s, i) => (s.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);
            var stageTitles = stageList.ToDictionary(s => s.Key, s => s.Title, StringComparer.Ordinal);
            int Rank(string stage) => stageOrder.GetValueOrDefault(stage, int.MaxValue);

            return new ChangeCounts(
                updated,
                filled,
                cardsWithFields,
                added,
                moved,
                fields.OrderByDescending(f => f.Value).ThenBy(f => order[f.Key])
                    .Select(f => new FieldCount(f.Key, titles[f.Key], f.Value)).ToList(),
                stages.OrderByDescending(s => s.Value).ThenBy(s => Rank(s.Key.From)).ThenBy(s => Rank(s.Key.To))
                    .Select(s => new StageMove(
                        s.Key.From, stageTitles.GetValueOrDefault(s.Key.From, s.Key.From),
                        s.Key.To, stageTitles.GetValueOrDefault(s.Key.To, s.Key.To), s.Value))
                    .ToList());
        }
    }
}
