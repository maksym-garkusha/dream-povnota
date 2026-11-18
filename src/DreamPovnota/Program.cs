using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DreamPovnota.Dream;
using DreamPovnota.Export;
using DreamPovnota.Report;
using DreamPovnota.Stats;
using DreamPovnota.Store;
using DreamPovnota.Sync;

// dotnet run -- sync   [--cache data] [--parallel 2] [--limit N]   забрати картки з API в кеш
// dotnet run -- export [--cache data] [--as-of дата] <тека сайту>  data.json для сторінки
// dotnet run -- report [--cache data] [--as-of дата] <файл.md>     звіт
// dotnet run -- digest --edition data.json [--history адреса] <тека> зріз для служб відновлення

Console.OutputEncoding = Encoding.UTF8;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (args.Length == 0) return Usage();

var options = Options.Parse(args[1..]);
if (options is null) return Usage();

switch (args[0])
{
    case "sync":
        return await RunSync(options, cts.Token);
    case "export" when options.Rest.Count == 1:
        return await RunExport(options, options.Rest[0], cts.Token);
    case "report" when options.Rest.Count == 1:
        return await RunReport(options, options.Rest[0], cts.Token);
    case "digest" when options.Rest.Count == 1 && options.Edition is not null:
        return await DigestCommand.RunAsync(options.Edition, options.History, options.Rest[0], Console.Out, cts.Token);
    default:
        return Usage();
}

static async Task<int> RunSync(Options options, CancellationToken ct)
{
    using var http = DreamClient.CreateHttpClient();
    var sync = new CardSync(new DreamClient(http), new CardStore(options.Cache)) { Log = Console.Out };
    var clock = Stopwatch.StartNew();

    SyncResult result;
    try
    {
        result = await sync.RunAsync(new SyncOptions(options.Parallel, options.Limit), ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
    {
        // сюди потрапляє тільки збій стрічки: без неї не знаємо, що оновлювати
        Console.Error.WriteLine($"Не вдалося прочитати стрічку: {ex.Message}");
        return 1;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        Console.Error.WriteLine("Зупинено, кеш не змінено");
        return 130;
    }

    Console.WriteLine();
    Print("Проєкти", result.Projects);
    Print("Об'єкти", result.Objects);
    if (result.ObjectsNotInFeed > 0)
        Console.WriteLine($"Об'єктів, яких немає в стрічці об'єктів: {result.ObjectsNotInFeed}");
    foreach (string id in result.UnparsedIds)
        Console.WriteLine($"Не розібрано: {id}");
    Console.WriteLine($"Час: {clock.Elapsed:hh\\:mm\\:ss}, кеш у {Path.GetFullPath(options.Cache)}");

    if (result.TooManyErrors)
    {
        Console.Error.WriteLine(
            $"Помилок {result.ErrorShare:P1}, це більше за {SyncResult.MaxErrorShare:P0}: дані неповні");
        return 1;
    }
    return 0;
}

static async Task<int> RunExport(Options options, string siteDirectory, CancellationToken ct)
{
    if (await LoadSummary(options, ct) is not { } summary) return 1;

    await DataExport.WriteAsync(summary, siteDirectory, ct);
    string path = Path.GetFullPath(Path.Combine(siteDirectory, DataExport.FileName));
    Console.WriteLine(
        $"Відкритих проєктів {summary.Open.Count}, організацій {summary.Organisations.Count}, " +
        $"середня наповненість {summary.All.AverageScore:P1}");
    Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} КБ");
    return 0;
}

static async Task<int> RunReport(Options options, string path, CancellationToken ct)
{
    if (await LoadSummary(options, ct) is not { } summary) return 1;

    await MarkdownReport.WriteAsync(summary, path, ct);
    Console.WriteLine($"Звіт: {Path.GetFullPath(path)}");
    return 0;
}

static async Task<Summary?> LoadSummary(Options options, CancellationToken ct)
{
    var store = new CardStore(options.Cache);
    var projects = await store.ReadProjectsAsync(ct);
    if (projects.Cards.Count == 0)
    {
        Console.Error.WriteLine($"У {Path.GetFullPath(options.Cache)} немає карток. Спершу: dotnet run -- sync --cache {options.Cache}");
        return null;
    }

    var objects = await store.ReadObjectsAsync(ct);
    var sync = await store.ReadSyncAsync(ct);
    if (sync is null)
        Console.WriteLine("У кеші немає sync.json: скільки було в стрічці і скільки не розібрано, невідомо");

    var asOf = options.AsOf ?? TimeProvider.System.GetUtcNow();
    Console.WriteLine($"Дата зрізу {asOf.UtcDateTime:yyyy-MM-dd HH:mm} UTC, проєктів у кеші {projects.Cards.Count}, об'єктів {objects.Cards.Count}");
    return Summary.Build(projects.Cards, objects.Cards, asOf, sync);
}

static void Print(string what, SyncCounts c) =>
    Console.WriteLine(
        $"{what}: у стрічці {c.InFeed}, завантажено {c.Downloaded}, з кешу {c.FromCache}, " +
        $"зникли (404) {c.Gone}, помилок завантаження {c.Failed}, не розібрано {c.Unparsed}, прибрано з кешу {c.Removed}");

static int Usage()
{
    Console.Error.WriteLine("""
        Використання:
          sync   [--cache data] [--parallel 2] [--limit N]   забрати картки з API в кеш
          export [--cache data] [--as-of дата] <тека сайту>  записати data.json для сторінки
          report [--cache data] [--as-of дата] <файл.md>     записати звіт у Markdown
          digest --edition data.json [--history адреса] <тека>
                                                       xlsx і PDF зрізу для агентства й служб відновлення

        --limit N бере тільки перші N проєктів стрічки, для швидкої перевірки;
        картки поза цими N з кешу прибираються.
        --as-of задає дату зрізу (типово зараз), наприклад 2026-11-17T22:00:00Z;
        від неї рахується, чи оновлювалась картка за останній рік.
        --history дає адресу веб-сервісу, звідки взяти стан карток на початок тижня,
        наприклад https://dream-povnota.onrender.com; без неї зріз без дозаповнених полів.
        """);
    return 2;
}

internal record Options(
    string Cache, int Parallel, int? Limit, DateTimeOffset? AsOf, IReadOnlyList<string> Rest,
    string? Edition = null, string? History = null)
{
    /// <summary>null, якщо параметр невідомий або без значення.</summary>
    public static Options? Parse(IReadOnlyList<string> args)
    {
        var options = new Options("data", 2, null, null, []);
        var rest = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string? value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--cache" when value is not null:
                    options = options with { Cache = value };
                    i++;
                    break;
                case "--parallel" when PositiveNumber(value) is { } parallel:
                    options = options with { Parallel = parallel };
                    i++;
                    break;
                case "--limit" when PositiveNumber(value) is { } limit:
                    options = options with { Limit = limit };
                    i++;
                    break;
                case "--as-of" when Date(value) is { } asOf:
                    options = options with { AsOf = asOf };
                    i++;
                    break;
                case "--edition" when value is not null:
                    options = options with { Edition = value };
                    i++;
                    break;
                case "--history" when Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http":
                    options = options with { History = value };
                    i++;
                    break;
                case var arg when arg.StartsWith("--", StringComparison.Ordinal):
                    return null;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }
        return options with { Rest = rest };
    }

    // дата без зони вважається UTC, щоб зріз не залежав від годинникового поясу машини
    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static int? PositiveNumber(string? value) =>
        int.TryParse(value, out int n) && n > 0 ? n : null;
}
