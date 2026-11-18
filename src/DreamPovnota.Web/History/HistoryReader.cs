using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace DreamPovnota.Web.History;

/// <summary>
/// Читання історії. Кожен метод повертає null, коли історії немає: база не
/// підключена, позначена недоступною після збою або щойно не відповіла. Сторінки
/// тоді показують поточний стан з поясненням, а не помилку.
/// </summary>
public sealed class HistoryReader(Db db, ILogger<HistoryReader> log)
{
    public const int FeedSnapshots = 30;

    public bool IsConfigured => db.IsConfigured;

    public Task<HistoryStatus?> StatusAsync(CancellationToken ct) => ReadAsync(async (c, t) =>
    {
        var row = await c.QuerySingleAsync<(long Count, DateTime? First, int? LastId, DateTime? Last)>(new CommandDefinition(
            "select count(*), min(generated), max(id), max(generated) from snapshot", transaction: t, cancellationToken: ct));
        return new HistoryStatus((int)row.Count, Utc(row.First), row.LastId, Utc(row.Last));
    }, ct);

    public Task<IReadOnlyList<SnapshotInfo>?> SnapshotsAsync(int limit, CancellationToken ct) => ReadAsync<IReadOnlyList<SnapshotInfo>>(async (c, t) =>
    {
        var rows = await c.QueryAsync<(int Id, DateTime Generated, DateTime? Synced, int Open, int Added, int Removed, int Changed, bool RulesChanged)>(
            new CommandDefinition(
                """
                select id, generated, synced, open_cards, added, removed, changed, rules_changed
                from snapshot order by id desc limit @limit
                """, new { limit }, t, cancellationToken: ct));
        return rows.Select(r => new SnapshotInfo(r.Id, Utc(r.Generated), Utc(r.Synced), r.Open, r.Added, r.Removed, r.Changed, r.RulesChanged)).ToList();
    }, ct);

    /// <summary>
    /// Зміни в останніх <paramref name="snapshots"/> знімках. Перший знімок (усе «додано»)
    /// і знімки зі зміною правил (усе «змінено») сюди не входять.
    /// </summary>
    public Task<IReadOnlyList<CardChange>?> ChangesAsync(HistoryScope scope, int snapshots, CancellationToken ct, int? snapshot = null) =>
        ReadAsync<IReadOnlyList<CardChange>>(async (c, t) =>
        {
            var parameters = new { org = scope.Organisation, region = scope.Region, snapshots, snapshot };
            var rules = (await c.QueryAsync<(int Id, string Checks)>(new CommandDefinition(
                    """
                    select s.id, r.checks::text from snapshot s join rules r on r.id = s.rules_id
                    """, parameters, t, cancellationToken: ct)))
                .ToDictionary(r => r.Id, r => Codes(r.Checks));

            var rows = await c.QueryAsync<ChangeRow>(new CommandDefinition(
                """
                with recent as (
                  select id from snapshot
                  where not rules_changed and id > (select min(id) from snapshot)
                    and (@snapshot::int is null or id = @snapshot::int)
                  order by id desc limit @snapshots
                ),
                touched as (
                  select distinct s.id as snapshot_id, v.card_id
                  from recent s
                  join card_version v on lower(v.valid_during) = s.id or upper(v.valid_during) = s.id
                  where (@org::text is null or v.org_id = (select id from organisation where key = @org::text))
                    and (@region::text is null or v.region = @region::text)
                )
                select t.snapshot_id as Snapshot, c.code as Code, c.title as Title,
                       o.stage as StageFrom, n.stage as StageTo,
                       o.applicable as OldApplicable, o.filled as OldFilled,
                       n.applicable as NewApplicable, n.filled as NewFilled
                from touched t
                join card c on c.id = t.card_id
                left join card_version n on n.card_id = t.card_id and lower(n.valid_during) = t.snapshot_id
                left join card_version o on o.card_id = t.card_id and upper(o.valid_during) = t.snapshot_id
                order by t.snapshot_id desc, c.title, c.code
                """, parameters, t, cancellationToken: ct));

            return rows.Select(r => r.ToChange(rules.GetValueOrDefault(r.Snapshot, []))).ToList();
        }, ct);

    /// <summary>Наповненість організації за знімками, порахована з версій карток.</summary>
    public Task<IReadOnlyList<TrendPoint>?> OrganisationTrendAsync(string key, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<TrendPoint>>(async (c, t) => Trend(await c.QueryAsync<TrendRow>(new CommandDefinition(
            """
            select s.id as Snapshot, s.generated as Generated, count(*)::int as Open,
                   (count(*) filter (where v.score_applicable > 0 and v.score_filled = v.score_applicable))::int as Complete,
                   avg(case when v.score_applicable = 0 then 0 else v.score_filled::float8 / v.score_applicable end) as AvgScore
            from organisation o
            join card_version v on v.org_id = o.id
            join snapshot s on v.valid_during @> s.id
            where o.key = @key
            group by s.id, s.generated
            order by s.id
            """, new { key }, t, cancellationToken: ct))), ct);

    /// <summary>Наповненість розрізу за знімками з slice_stat: dimension 'a', 'g', 'r' чи 's'.</summary>
    public Task<IReadOnlyList<TrendPoint>?> SliceTrendAsync(char dimension, string key, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<TrendPoint>>(async (c, t) => Trend(await c.QueryAsync<TrendRow>(new CommandDefinition(
            """
            select s.id as Snapshot, s.generated as Generated, st.open_cards as Open,
                   st.complete_cards as Complete, st.avg_score as AvgScore
            from slice_stat st join snapshot s on s.id = st.snapshot_id
            where st.dimension = @dimension and st.key = @key
            order by s.id
            """, new { dimension = dimension.ToString(), key }, t, cancellationToken: ct))), ct);

    public Task<IReadOnlyList<CardHistoryPoint>?> CardAsync(string code, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<CardHistoryPoint>>(async (c, t) =>
        {
            var rows = await c.QueryAsync<(int Snapshot, DateTime Generated, string Stage, short Filled, short Applicable)>(new CommandDefinition(
                """
                select lower(v.valid_during), s.generated, v.stage, v.score_filled, v.score_applicable
                from card c
                join card_version v on v.card_id = c.id
                join snapshot s on s.id = lower(v.valid_during)
                where c.code = @code
                order by 1
                """, new { code }, t, cancellationToken: ct));
            return rows.Select(r => new CardHistoryPoint(r.Snapshot, Utc(r.Generated), r.Stage, r.Filled, r.Applicable)).ToList();
        }, ct);

    private async Task<T?> ReadAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> read, CancellationToken ct)
        where T : class
    {
        if (!db.IsAvailable) return null;
        try
        {
            await using var connection = await db.Source!.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await connection.ExecuteAsync(new CommandDefinition("set transaction read only", transaction: transaction, cancellationToken: ct));
            var result = await read(connection, transaction);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (Db.IsOutage(ex) && !ct.IsCancellationRequested)
        {
            db.MarkDown();
            log.LogWarning("База історії не відповіла: {Error}", ex.GetType().Name);
            return null;
        }
    }

    private static IReadOnlyList<TrendPoint> Trend(IEnumerable<TrendRow> rows) =>
        rows.Select(r => new TrendPoint(r.Snapshot, Utc(r.Generated), r.Open, r.Complete, Math.Round(r.AvgScore, 6))).ToList();

    private static string[] Codes(string checksJson)
    {
        using var document = JsonDocument.Parse(checksJson);
        return document.RootElement.EnumerateArray().Select(c => c.GetProperty("code").GetString()!).ToArray();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    private sealed record TrendRow(int Snapshot, DateTime Generated, int Open, int Complete, double AvgScore);

    private sealed record ChangeRow(
        int Snapshot, string Code, string Title, string? StageFrom, string? StageTo,
        int? OldApplicable, int? OldFilled, int? NewApplicable, int? NewFilled)
    {
        public CardChange ToChange(string[] codes)
        {
            var kind = OldApplicable is null ? ChangeKind.Added : NewApplicable is null ? ChangeKind.Removed : ChangeKind.Changed;
            int oldFilled = OldFilled ?? 0, newFilled = NewFilled ?? 0;
            int oldApplicable = OldApplicable ?? 0, newApplicable = NewApplicable ?? 0;
            bool bothSides = kind == ChangeKind.Changed;
            return new CardChange(
                Snapshot, Code, Title, kind, StageFrom, StageTo,
                bothSides ? Bits(newFilled & ~oldFilled, codes) : [],
                bothSides ? Bits(oldFilled & ~newFilled, codes) : [],
                bothSides ? Bits(newApplicable & ~oldApplicable, codes) : []);
        }

        private static List<string> Bits(int mask, string[] codes) =>
            codes.Where((_, i) => (mask & (1 << i)) != 0).ToList();
    }
}
