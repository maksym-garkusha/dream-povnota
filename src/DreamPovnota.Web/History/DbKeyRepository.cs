using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace DreamPovnota.Web.History;

/// <summary>
/// Сховище ключів Data Protection у PostgreSQL. Ключі підписують тільки токени форм
/// і персональних даних не захищають, тому лежать без шифрування.
/// Інтерфейс IXmlRepository синхронний, отже й запити тут синхронні. Без DATABASE_URL,
/// поки таблиці ще немає (перший старт до міграції) або коли база недоступна, ключі
/// живуть у пам'яті процесу: форма працює, а наступний старт видасть нові токени.
/// </summary>
public sealed class DbKeyRepository(Db db, ILogger<DbKeyRepository> log) : IXmlRepository
{
    private readonly List<XElement> unsaved = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elements = new List<XElement>();
        if (db.IsAvailable)
        {
            try
            {
                using var connection = db.Source!.OpenConnection();
                elements.AddRange(connection.Query<string>("select xml from data_protection_key order by created_at").Select(XElement.Parse));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                log.LogInformation("Таблиці ключів форм ще немає, до міграції ключі в пам'яті");
            }
            catch (Exception ex) when (Db.IsOutage(ex))
            {
                log.LogWarning(ex, "Ключі форм не прочитано з бази");
            }
        }

        lock (unsaved) elements.AddRange(unsaved.Where(u => elements.All(e => Name(e) != Name(u))));
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        if (db.IsAvailable)
        {
            try
            {
                using var connection = db.Source!.OpenConnection();
                connection.Execute(
                    "insert into data_protection_key (name, xml) values (@friendlyName, @xml) on conflict (name) do nothing",
                    new { friendlyName, xml = element.ToString(SaveOptions.DisableFormatting) });
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                log.LogInformation("Таблиці ключів форм ще немає, ключ перенесемо в базу після міграції");
            }
            catch (Exception ex) when (Db.IsOutage(ex))
            {
                log.LogWarning(ex, "Ключ форм не збережено в базі, лишаємо в пам'яті");
            }
        }

        lock (unsaved) unsaved.Add(element);
    }

    /// <summary>
    /// Дописує в базу ключі, створені до появи таблиці: набір ключів створюється
    /// ще на старті, а міграція йде вже після завантаження випуску.
    /// </summary>
    public async Task SaveUnsavedAsync(CancellationToken ct)
    {
        XElement[] pending;
        lock (unsaved) pending = [.. unsaved];
        // викликається одразу після вдалої міграції, тож на позначку недоступності не зважаємо
        if (pending.Length == 0 || db.Source is null) return;

        await using var connection = await db.Source!.OpenConnectionAsync(ct);
        foreach (var element in pending)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into data_protection_key (name, xml) values (@name, @xml) on conflict (name) do nothing",
                new { name = "key-" + Name(element), xml = element.ToString(SaveOptions.DisableFormatting) },
                cancellationToken: ct));
            lock (unsaved) unsaved.Remove(element);
        }
        log.LogInformation("Ключів форм перенесено в базу: {Count}", pending.Length);
    }

    private static string? Name(XElement element) => element.Attribute("id")?.Value;
}
