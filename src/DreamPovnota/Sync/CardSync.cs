using System.Collections.Concurrent;
using System.Text.Json;
using DreamPovnota.Cards;
using DreamPovnota.Dream;
using DreamPovnota.Store;

namespace DreamPovnota.Sync;

/// <param name="Parallel">Скільки карток вантажимо одночасно.</param>
/// <param name="Limit">Взяти тільки перші N проєктів стрічки, для швидкої перевірки.</param>
public record SyncOptions(int Parallel = 4, int? Limit = null);

/// <param name="InFeed">Скільки записів у стрічці (для об'єктів: потрібних і наявних у стрічці).</param>
/// <param name="Downloaded">Завантажено нових або змінених.</param>
/// <param name="FromCache">Не змінились, взято з кешу.</param>
/// <param name="Gone">Є в стрічці, але картка відповіла 404.</param>
/// <param name="Failed">Не вдалося завантажити після всіх спроб.</param>
/// <param name="Unparsed">Завантажено, але не вдалося розібрати.</param>
/// <param name="Removed">Були в кеші, але зникли зі стрічки (чи більше не потрібні).</param>
public record SyncCounts(int InFeed, int Downloaded, int FromCache, int Gone, int Failed, int Unparsed, int Removed)
{
    public int Errors => Failed + Unparsed;
}

/// <param name="ObjectsNotInFeed">На скільки об'єктів посилаються відкриті проєкти, але їх немає в стрічці об'єктів.</param>
/// <param name="UnparsedIds">Ідентифікатори карток, які не вдалося розібрати.</param>
public record SyncResult(
    SyncCounts Projects,
    SyncCounts Objects,
    int ObjectsNotInFeed,
    IReadOnlyList<string> UnparsedIds)
{
    public const double MaxErrorShare = 0.02;

    public double ErrorShare
    {
        get
        {
            int total = Projects.InFeed + Objects.InFeed;
            return total == 0 ? 0 : (double)(Projects.Errors + Objects.Errors) / total;
        }
    }

    /// <summary>Помилок більше 2%: такі дані краще не публікувати.</summary>
    public bool TooManyErrors => ErrorShare > MaxErrorShare;
}

/// <summary>
/// Стрічка → що змінилось проти кешу → дозавантажити тільки змінене. Об'єкти
/// беремо тільки ті, на які посилаються відкриті проєкти: решта для перевірок не потрібна.
/// </summary>
public sealed class CardSync(DreamClient client, CardStore store)
{
    public TextWriter Log { get; init; } = TextWriter.Null;

    public async Task<SyncResult> RunAsync(SyncOptions options, CancellationToken ct = default)
    {
        var cachedProjects = await ReadCacheAsync(store.ReadProjectsAsync(ct), p => p.Id, CardStore.ProjectsFile);
        var cachedObjects = await ReadCacheAsync(store.ReadObjectsAsync(ct), o => o.Id, CardStore.ObjectsFile);
        var unparsed = new ConcurrentBag<string>();

        var projectFeed = await ReadFeedAsync(client.ProjectFeedAsync(ct), options.Limit);
        Log.WriteLine($"У стрічці проєктів {projectFeed.Count}, у кеші {cachedProjects.Count}");

        var (projects, projectCounts) = await RefreshAsync(
            projectFeed, cachedProjects, p => p.Updated,
            client.GetProjectAsync, CardParser.ParseProject, unparsed, options, "проєктів", ct);

        var wanted = projects.Where(p => p.IsOpen).SelectMany(p => p.ObjectIds).ToHashSet();
        var objectFeed = (await ReadFeedAsync(client.ObjectFeedAsync(ct), null)).ToDictionary(e => e.Id);
        var wantedFeed = wanted.Where(objectFeed.ContainsKey).Select(id => objectFeed[id]).ToList();
        Log.WriteLine($"У стрічці об'єктів {objectFeed.Count}, з них потрібні відкритим проєктам {wantedFeed.Count}");

        var (objects, objectCounts) = await RefreshAsync(
            wantedFeed, cachedObjects, o => o.Updated,
            client.GetObjectAsync, CardParser.ParseObject, unparsed, options, "об'єктів", ct);

        await store.WriteProjectsAsync(projects.OrderBy(p => p.Updated).ThenBy(p => p.Id, StringComparer.Ordinal), ct);
        await store.WriteObjectsAsync(objects.OrderBy(o => o.Updated).ThenBy(o => o.Id, StringComparer.Ordinal), ct);

        return new SyncResult(
            projectCounts,
            objectCounts,
            wanted.Count - wantedFeed.Count,
            unparsed.Order(StringComparer.Ordinal).ToList());
    }

    private async Task<Dictionary<string, T>> ReadCacheAsync<T>(
        Task<StoreRead<T>> reading, Func<T, string> id, string name)
    {
        var read = await reading;
        if (read.BadLines > 0)
            Log.WriteLine($"У {name} не вдалося прочитати рядків: {read.BadLines}, ці картки завантажимо знову");

        var cards = new Dictionary<string, T>();
        foreach (var card in read.Cards) cards[id(card)] = card;
        return cards;
    }

    private static async Task<List<FeedEntry>> ReadFeedAsync(IAsyncEnumerable<FeedEntry> feed, int? limit)
    {
        // той самий id двічі в стрічці не очікуємо, але якщо трапиться, лишаємо пізніший запис
        var entries = new Dictionary<string, FeedEntry>();
        await foreach (var entry in feed)
        {
            if (limit is { } n && entries.Count >= n && !entries.ContainsKey(entry.Id)) break;
            entries[entry.Id] = entry;
        }
        return entries.Values.ToList();
    }

    private enum Outcome { Loaded, Gone, Failed, Unparsed }

    private async Task<(List<T> Cards, SyncCounts Counts)> RefreshAsync<T>(
        List<FeedEntry> feed,
        Dictionary<string, T> cached,
        Func<T, DateTimeOffset> updated,
        Func<string, CancellationToken, Task<string?>> download,
        Func<string, T> parse,
        ConcurrentBag<string> unparsed,
        SyncOptions options,
        string what,
        CancellationToken ct) where T : class
    {
        var stale = feed
            .Where(e => !cached.TryGetValue(e.Id, out var card) || updated(card) < e.Updated)
            .ToList();
        Log.WriteLine($"Завантажуємо {what}: {stale.Count}");

        var results = new ConcurrentDictionary<string, (Outcome Outcome, T? Card)>();
        int done = 0;

        await Parallel.ForEachAsync(
            stale,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallel), CancellationToken = ct },
            async (entry, token) =>
            {
                results[entry.Id] = await LoadAsync(entry.Id, download, parse, unparsed, token);
                int count = Interlocked.Increment(ref done);
                if (count % 1000 == 0) Log.WriteLine($"  {what}: {count} з {stale.Count}");
            });

        var cards = new List<T>(feed.Count);
        int downloaded = 0, fromCache = 0, gone = 0, failed = 0, broken = 0;

        foreach (var entry in feed)
        {
            cached.TryGetValue(entry.Id, out var old);
            if (!results.TryGetValue(entry.Id, out var result))
            {
                cards.Add(old!);
                fromCache++;
                continue;
            }

            switch (result.Outcome)
            {
                case Outcome.Loaded:
                    cards.Add(result.Card!);
                    downloaded++;
                    break;
                case Outcome.Gone:
                    gone++;
                    break;
                case Outcome.Failed or Outcome.Unparsed:
                    // стара версія краща, ніж ніякої; наступний запуск спробує знову
                    if (old is not null) cards.Add(old);
                    if (result.Outcome == Outcome.Failed) failed++;
                    else broken++;
                    break;
            }
        }

        var inFeed = feed.Select(e => e.Id).ToHashSet();
        int removed = cached.Keys.Count(id => !inFeed.Contains(id));

        return (cards, new SyncCounts(feed.Count, downloaded, fromCache, gone, failed, broken, removed));
    }

    private async Task<(Outcome, T?)> LoadAsync<T>(
        string id,
        Func<string, CancellationToken, Task<string?>> download,
        Func<string, T> parse,
        ConcurrentBag<string> unparsed,
        CancellationToken ct) where T : class
    {
        string? json;
        try
        {
            json = await download(id, ct);
        }
        catch (HttpRequestException ex)
        {
            Log.WriteLine($"  не завантажено {id}: {ex.Message}");
            return (Outcome.Failed, null);
        }

        if (json is null) return (Outcome.Gone, null);

        try
        {
            return (Outcome.Loaded, parse(json));
        }
        catch (JsonException ex)
        {
            Log.WriteLine($"  не розібрано {id}: {ex.Message}");
            unparsed.Add(id);
            return (Outcome.Unparsed, null);
        }
    }
}
