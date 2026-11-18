using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DreamPovnota.Export;
using DreamPovnota.Web.Import;
using Npgsql;
using NpgsqlTypes;

namespace DreamPovnota.Web.History;

/// <summary>
/// Записує випуск data.json знімком в одній транзакції. Зберігаються тільки зміни:
/// нова версія картки з'являється, коли змінилось щось із організації, області,
/// стадії, сектору, дати, масок чи пошуку фінансування, або змінились самі правила.
/// Той самий випуск удруге (чи старіший після відкату Pages) нічого не додає.
/// </summary>
public static class SnapshotWriter
{
    private const long LockKey = 727102;

    /// <returns>Номер нового знімка або null, якщо такий чи новіший випуск уже є.</returns>
    public static async Task<int?> WriteAsync(Db db, LoadedEdition loaded, CancellationToken ct)
    {
        var edition = loaded.Edition;
        await using var connection = await db.Source!.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        Task<int> Execute(string sql, object? parameters = null) =>
            connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        Task<T> Scalar<T>(string sql, object? parameters = null) =>
            connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct))!;

        await Execute("select pg_advisory_xact_lock(@LockKey)", new { LockKey });
        bool known = await Scalar<bool>(
            "select exists (select 1 from snapshot where generated >= @Generated)", new { edition.Generated });
        if (known) return null;

        string rulesJson = JsonSerializer.Serialize(edition.Checks.Select(c => new { code = c.Code, title = c.Title, score = c.CountsInScore }));
        byte[] rulesHash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', edition.Checks.Select(c => $"{c.Code}|{c.CountsInScore}"))));
        // update на конфлікті потрібен, щоб returning повернув id і для наявного рядка
        short rulesId = await Scalar<short>(
            """
            insert into rules (hash, checks) values (@rulesHash, @rulesJson::jsonb)
            on conflict (hash) do update set hash = excluded.hash
            returning id
            """, new { rulesHash, rulesJson });
        short? previousRules = await Scalar<short?>("select rules_id from snapshot order by id desc limit 1");
        bool rulesChanged = previousRules is not null && previousRules != rulesId;

        int n = await Scalar<int>(
            """
            insert into snapshot (generated, synced, content_hash, rules_id, rules_changed, open_cards)
            values (@Generated, @Synced, @hash, @rulesId, @rulesChanged, @open)
            returning id
            """,
            new
            {
                edition.Generated,
                edition.Synced,
                hash = loaded.ContentHash,
                rulesId,
                rulesChanged,
                open = edition.Cards.Count,
            });

        await Execute(
            """
            create temp table incoming (
              code text, title text, org_key text, edrpou text, org_title text, region text, stage text,
              sector text, updated date, applicable int, filled int, score_applicable smallint, score_filled smallint,
              seeks boolean) on commit drop
            """);
        await CopyAsync(connection, loaded, ct);

        await Execute(
            """
            insert into organisation (key, edrpou, title, first_snapshot, last_snapshot)
            select distinct on (org_key) org_key, edrpou, org_title, @n, @n from incoming order by org_key
            on conflict (key) do update set title = excluded.title, last_snapshot = @n
            """, new { n });
        await Execute(
            """
            insert into card (code, title, first_snapshot, last_snapshot)
            select code, title, @n, @n from incoming
            on conflict (code) do update set title = excluded.title, last_snapshot = @n
            """, new { n });

        // закриваємо поточні версії карток, яких немає у випуску або які змінились
        await Execute(
            """
            update card_version v set valid_during = int4range(lower(v.valid_during), @n)
            from card c
            left join incoming i on i.code = c.code
            left join organisation o on o.key = i.org_key
            where v.card_id = c.id and upper_inf(v.valid_during)
              and (@rulesChanged or i.code is null
                   or (v.org_id, v.region, v.stage, v.sector, v.updated, v.applicable, v.filled, v.seeks_funding)
                      is distinct from (o.id, i.region, i.stage, i.sector, i.updated, i.applicable, i.filled, i.seeks))
            """, new { n, rulesChanged });
        await Execute(
            """
            insert into card_version (card_id, valid_during, org_id, region, stage, sector, updated,
                                      applicable, filled, score_applicable, score_filled, seeks_funding)
            select c.id, int4range(@n, null), o.id, i.region, i.stage, i.sector, i.updated,
                   i.applicable, i.filled, i.score_applicable, i.score_filled, i.seeks
            from incoming i
            join card c on c.code = i.code
            join organisation o on o.key = i.org_key
            where not exists (select 1 from card_version v where v.card_id = c.id and upper_inf(v.valid_during))
            """, new { n });

        await Execute(
            """
            insert into slice_stat (snapshot_id, dimension, key, title, open_cards, complete_cards, avg_score, checks)
            values (@n, @Dimension, @Key, @Title, @Open, @Complete, @AverageScore, @Checks)
            """,
            Slices(edition).Select(s => new { n, s.Dimension, s.Key, s.Title, s.Open, s.Complete, s.AverageScore, s.Checks }));

        await Execute(
            """
            update snapshot set
              added = (select count(*) from card_version v where lower(v.valid_during) = @n
                       and not exists (select 1 from card_version p where p.card_id = v.card_id and upper(p.valid_during) = @n)),
              removed = (select count(*) from card_version v where upper(v.valid_during) = @n
                         and not exists (select 1 from card_version s where s.card_id = v.card_id and lower(s.valid_during) = @n)),
              changed = (select count(*) from card_version v where upper(v.valid_during) = @n
                         and exists (select 1 from card_version s where s.card_id = v.card_id and lower(s.valid_during) = @n))
            where id = @n
            """, new { n });

        await transaction.CommitAsync(ct);
        return n;
    }

    private static async Task CopyAsync(NpgsqlConnection connection, LoadedEdition loaded, CancellationToken ct)
    {
        var edition = loaded.Edition;
        await using var copy = await connection.BeginBinaryImportAsync(
            "copy incoming (code, title, org_key, edrpou, org_title, region, stage, sector, updated, " +
            "applicable, filled, score_applicable, score_filled, seeks) from stdin (format binary)", ct);
        foreach (var card in edition.Cards)
        {
            var organisation = edition.Organisations[card.Organisation];
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(card.Code, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(card.Title, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(organisation.Key, NpgsqlDbType.Text, ct);
            if (organisation.Edrpou is { } edrpou) await copy.WriteAsync(edrpou, NpgsqlDbType.Text, ct);
            else await copy.WriteNullAsync(ct);
            await copy.WriteAsync(organisation.Title, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(edition.Regions[card.Region].Key, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(edition.Stages[card.Stage].Key, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(edition.Sectors[card.Sector].Key, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(card.Updated, NpgsqlDbType.Date, ct);
            await copy.WriteAsync(card.Applicable, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(card.Filled, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync((short)loaded.ScoreApplicable(card), NpgsqlDbType.Smallint, ct);
            await copy.WriteAsync((short)loaded.ScoreFilled(card), NpgsqlDbType.Smallint, ct);
            await copy.WriteAsync(card.SeeksFunding, NpgsqlDbType.Boolean, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private sealed record SliceRow(string Dimension, string Key, string Title, int Open, int Complete, double AverageScore, int[] Checks);

    private static IEnumerable<SliceRow> Slices(Edition edition)
    {
        static SliceRow Row(string dimension, EditionSlice s) => new(
            dimension, s.Key, s.Title, s.Open, s.Complete, s.AverageScore,
            s.Checks.SelectMany(t => new[] { t.Applicable, t.Filled }).ToArray());

        yield return Row("a", edition.All);
        foreach (var s in edition.Stages) yield return Row("g", s);
        foreach (var s in edition.Regions) yield return Row("r", s);
        foreach (var s in edition.Sectors) yield return Row("s", s);
    }
}
