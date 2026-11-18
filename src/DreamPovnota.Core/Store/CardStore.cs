using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using DreamPovnota.Cards;

namespace DreamPovnota.Store;

/// <param name="BadLines">Рядки, які не вдалося прочитати. Ці картки синхронізація завантажить знову.</param>
public record StoreRead<T>(IReadOnlyList<T> Cards, int BadLines);

/// <summary>
/// Що відомо про останню синхронізацію, але не видно з самих карток: скільки
/// було в стрічці і що не вдалося завантажити чи розібрати.
/// </summary>
/// <param name="ProjectsInFeed">Проєктів у стрічці (з --limit це N).</param>
/// <param name="ProjectsFailed">Не завантажено після всіх спроб; у кеші лишилась стара версія, якщо була.</param>
/// <param name="ProjectsGone">Були в стрічці, але картка відповіла 404.</param>
/// <param name="UnparsedIds">Проєкти й об'єкти, які не вдалося розібрати.</param>
/// <param name="ObjectsNotInFeed">На скільки об'єктів посилаються відкриті проєкти, але в стрічці об'єктів їх немає.</param>
public record SyncInfo(
    DateTimeOffset Finished,
    int ProjectsInFeed,
    int ProjectsFailed,
    int ProjectsGone,
    IReadOnlyList<string> UnparsedIds,
    int ObjectsNotInFeed);

/// <summary>
/// Кеш розібраних карток: projects.jsonl і objects.jsonl, по картці в рядку.
/// Сирих відповідей API тут немає, тож немає і персональних даних.
/// </summary>
public sealed class CardStore(string directory)
{
    public const string ProjectsFile = "projects.jsonl";
    public const string ObjectsFile = "objects.jsonl";
    public const string SyncFile = "sync.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // без цього кирилиця в кеші перетворюється на РА...
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Directory { get; } = directory;

    public Task<StoreRead<ProjectCard>> ReadProjectsAsync(CancellationToken ct = default) =>
        ReadAsync<ProjectCard>(ProjectsFile, ct);

    public Task<StoreRead<ObjectCard>> ReadObjectsAsync(CancellationToken ct = default) =>
        ReadAsync<ObjectCard>(ObjectsFile, ct);

    public Task WriteProjectsAsync(IEnumerable<ProjectCard> cards, CancellationToken ct = default) =>
        WriteAsync(ProjectsFile, cards, ct);

    public Task WriteObjectsAsync(IEnumerable<ObjectCard> cards, CancellationToken ct = default) =>
        WriteAsync(ObjectsFile, cards, ct);

    /// <summary>Підсумок останньої синхронізації або null, якщо його немає (кеш старший за цей файл).</summary>
    public async Task<SyncInfo?> ReadSyncAsync(CancellationToken ct = default)
    {
        string path = Path.Combine(Directory, SyncFile);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SyncInfo>(stream, JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task WriteSyncAsync(SyncInfo info, CancellationToken ct = default) =>
        WriteAtomicAsync(SyncFile, writer => writer.WriteAsync(JsonSerializer.Serialize(info, JsonOptions)), ct);

    private async Task<StoreRead<T>> ReadAsync<T>(string file, CancellationToken ct)
    {
        string path = Path.Combine(Directory, file);
        if (!File.Exists(path)) return new StoreRead<T>([], 0);

        var cards = new List<T>();
        int bad = 0;
        await foreach (string line in File.ReadLinesAsync(path, ct))
        {
            if (line.Length == 0) continue;
            try
            {
                if (JsonSerializer.Deserialize<T>(line, JsonOptions) is { } card) cards.Add(card);
                else bad++;
            }
            catch (JsonException)
            {
                // наприклад, обірваний останній рядок; картку просто завантажимо знову
                bad++;
            }
        }
        return new StoreRead<T>(cards, bad);
    }

    /// <summary>
    /// Пише в тимчасовий файл поруч і тоді перейменовує, щоб перерваний запуск
    /// не лишив напівзаписаний кеш.
    /// </summary>
    private Task WriteAsync<T>(string file, IEnumerable<T> cards, CancellationToken ct) =>
        WriteAtomicAsync(file, async writer =>
        {
            foreach (var card in cards)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(card, JsonOptions));
            }
        }, ct);

    private async Task WriteAtomicAsync(string file, Func<StreamWriter, Task> write, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = Path.Combine(Directory, file);
        string temp = path + ".tmp";

        await using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
        {
            await write(writer);
        }

        ct.ThrowIfCancellationRequested();
        File.Move(temp, path, overwrite: true);
    }
}
