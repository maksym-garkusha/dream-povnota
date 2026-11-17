using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DreamPovnota.Dream;
using DreamPovnota.Store;
using DreamPovnota.Sync;

// dotnet run -- sync   [--cache data] [--parallel 4] [--limit N]   забрати картки з API в кеш
// dotnet run -- export [--cache data] <тека сайту>                 data.json для сторінки
// dotnet run -- report [--cache data] <файл.md>                    звіт

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
    case "export":
    case "report":
        Console.Error.WriteLine($"Команда {args[0]} ще не реалізована");
        return 1;
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
    catch (Exception ex) when (ex is HttpRequestException or JsonException)
    {
        // сюди потрапляє тільки збій стрічки: без неї не знаємо, що оновлювати
        Console.Error.WriteLine($"Не вдалося прочитати стрічку: {ex.Message}");
        return 1;
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

static void Print(string what, SyncCounts c) =>
    Console.WriteLine(
        $"{what}: у стрічці {c.InFeed}, завантажено {c.Downloaded}, з кешу {c.FromCache}, " +
        $"зникли (404) {c.Gone}, помилок завантаження {c.Failed}, не розібрано {c.Unparsed}, прибрано з кешу {c.Removed}");

static int Usage()
{
    Console.Error.WriteLine("""
        Використання:
          sync   [--cache data] [--parallel 4] [--limit N]   забрати картки з API в кеш
          export [--cache data] <тека сайту>                 ще не реалізовано
          report [--cache data] <файл.md>                    ще не реалізовано

        --limit N бере тільки перші N проєктів стрічки, для швидкої перевірки;
        картки поза цими N з кешу прибираються.
        """);
    return 2;
}

internal record Options(string Cache, int Parallel, int? Limit, IReadOnlyList<string> Rest)
{
    /// <summary>null, якщо параметр невідомий або без значення.</summary>
    public static Options? Parse(IReadOnlyList<string> args)
    {
        var options = new Options("data", 4, null, []);
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
                case var arg when arg.StartsWith("--", StringComparison.Ordinal):
                    return null;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }
        return options with { Rest = rest };
    }

    private static int? PositiveNumber(string? value) =>
        int.TryParse(value, out int n) && n > 0 ? n : null;
}
