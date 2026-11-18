using Npgsql;

namespace DreamPovnota.Web.History;

/// <summary>
/// База історії. Адресу дає тільки змінна DATABASE_URL у форматі postgres://, як її
/// видають Render і Neon; переїзд на іншу базу це заміна однієї змінної.
/// Без DATABASE_URL історія вимкнена, а все інше працює.
/// </summary>
public sealed class Db : IAsyncDisposable
{
    /// <summary>Скільки не чіпати базу після збою, щоб запити не чекали тайм-аутів підключення.</summary>
    public static readonly TimeSpan DownFor = TimeSpan.FromMinutes(5);

    private readonly TimeProvider clock;
    private long downUntilTicks;

    public Db(IConfiguration configuration, TimeProvider clock, ILogger<Db> log)
    {
        this.clock = clock;
        if (configuration["DATABASE_URL"] is not { Length: > 0 } url) return;

        var (connectionString, ignored) = ToConnectionString(url);
        foreach (string name in ignored) log.LogInformation("Параметр DATABASE_URL {Name} пропущено", name);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        log.LogInformation("База історії: {Host}", builder.Host);
        Source = NpgsqlDataSource.Create(connectionString);
    }

    public NpgsqlDataSource? Source { get; }

    public bool IsConfigured => Source is not null;

    public bool IsAvailable => Source is not null && clock.GetUtcNow().UtcTicks >= Interlocked.Read(ref downUntilTicks);

    public void MarkDown() => Interlocked.Exchange(ref downUntilTicks, (clock.GetUtcNow() + DownFor).UtcTicks);

    public void MarkUp() => Interlocked.Exchange(ref downUntilTicks, 0);

    /// <summary>Чи варто рахувати виняток збоєм бази, а не помилкою в коді.</summary>
    public static bool IsOutage(Exception ex) => ex is NpgsqlException or TimeoutException;

    /// <summary>postgres://user:pass@host:port/db?sslmode=require у рядок підключення Npgsql.</summary>
    /// <returns>Рядок і назви параметрів запиту, яких Npgsql тут не бере.</returns>
    public static (string ConnectionString, IReadOnlyList<string> Ignored) ToConnectionString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql"))
            throw new FormatException("DATABASE_URL має бути у форматі postgres://користувач:пароль@сервер/база");

        string[] userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            MaxPoolSize = 5,
            Timeout = 5,
            CommandTimeout = 30,
            ConnectionIdleLifetime = 60,
        };

        var ignored = new List<string>();
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string name = Uri.UnescapeDataString(parts[0]);
            string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (name)
            {
                case "sslmode" when Enum.TryParse<SslMode>(value.Replace("-", ""), ignoreCase: true, out var ssl):
                    builder.SslMode = ssl;
                    break;
                case "channel_binding" when Enum.TryParse<ChannelBinding>(value, ignoreCase: true, out var binding):
                    builder.ChannelBinding = binding;
                    break;
                default:
                    ignored.Add(name);
                    break;
            }
        }
        return (builder.ConnectionString, ignored);
    }

    public async ValueTask DisposeAsync()
    {
        if (Source is not null) await Source.DisposeAsync();
    }
}
