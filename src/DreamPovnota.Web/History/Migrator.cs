using System.Reflection;
using Dapper;

namespace DreamPovnota.Web.History;

/// <summary>
/// Застосовує SQL-міграції з ресурсів збірки в одній транзакції під advisory lock,
/// тож два екземпляри під час розгортання не мігрують одночасно. Застосований файл
/// не змінюють; зміна схеми це новий файл 0002_....sql.
/// </summary>
public static class Migrator
{
    private const long LockKey = 727101;

    public static IReadOnlyList<(int Version, string Sql)> Migrations { get; } = Load();

    public static async Task<int> MigrateAsync(Db db, CancellationToken ct)
    {
        await using var connection = await db.Source!.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(@LockKey)", new { LockKey }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "create table if not exists schema_migration (version int primary key, applied_at timestamptz not null default now())",
            transaction: transaction, cancellationToken: ct));
        var applied = (await connection.QueryAsync<int>(new CommandDefinition(
            "select version from schema_migration", transaction: transaction, cancellationToken: ct))).ToHashSet();

        int count = 0;
        foreach (var (version, sql) in Migrations.Where(m => !applied.Contains(m.Version)))
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into schema_migration (version) values (@version)", new { version }, transaction, cancellationToken: ct));
            count++;
        }

        await transaction.CommitAsync(ct);
        return count;
    }

    // ресурси називаються DreamPovnota.Web.History.Migrations.0001_init.sql
    private static List<(int, string)> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string prefix = "DreamPovnota.Web.History.Migrations.";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                string file = n[prefix.Length..];
                return (int.Parse(file[..4], System.Globalization.CultureInfo.InvariantCulture), reader.ReadToEnd());
            })
            .OrderBy(m => m.Item1)
            .ToList();
    }
}
