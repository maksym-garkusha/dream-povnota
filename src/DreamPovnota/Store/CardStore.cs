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
/// Кеш розібраних карток: projects.jsonl і objects.jsonl, по картці в рядку.
/// Сирих відповідей API тут немає, тож немає і персональних даних.
/// </summary>
public sealed class CardStore(string directory)
{
    public const string ProjectsFile = "projects.jsonl";
    public const string ObjectsFile = "objects.jsonl";

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
    private async Task WriteAsync<T>(string file, IEnumerable<T> cards, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = Path.Combine(Directory, file);
        string temp = path + ".tmp";

        await using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
        {
            foreach (var card in cards)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(card, JsonOptions));
            }
        }

        File.Move(temp, path, overwrite: true);
    }
}
