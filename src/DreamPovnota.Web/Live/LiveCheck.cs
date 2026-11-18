using System.Text.Json;
using System.Text.RegularExpressions;
using DreamPovnota.Cards;
using DreamPovnota.Checks;
using DreamPovnota.Dream;
using DreamPovnota.Geo;
using DreamPovnota.Stats;
using Microsoft.Extensions.Caching.Memory;

namespace DreamPovnota.Web.Live;

public enum LiveStatus
{
    Found,
    NotFound,
    TooManyObjects,
    Unavailable,
}

/// <summary>Результат живої перевірки. Імен людей і кодів підприємців тут немає: їх відкидає розбір.</summary>
/// <param name="Code">Код без "DREAM-UA-".</param>
/// <param name="Edrpou">null для підприємця і для іноземної організації.</param>
public sealed record LiveResult(
    string Code,
    string Title,
    string OrganisationTitle,
    string? Edrpou,
    string RegionTitle,
    string StageTitle,
    bool IsOpen,
    IReadOnlyList<Check> Filled,
    IReadOnlyList<Check> Missing,
    int ScoreApplicable,
    int ScoreFilled,
    int MissingObjects,
    DateTimeOffset CheckedAt);

public sealed record LiveOutcome(LiveStatus Status, LiveResult? Result = null);

/// <summary>
/// Перевіряє одну картку просто зараз: проєкт і його об'єкти з API DREAM,
/// ті самі правила, що для data.json. Результат кешується на 10 хвилин,
/// «немає такого» на 2, щоб повторні натискання не йшли в DREAM.
/// </summary>
public sealed partial class LiveCheck(HttpClient http, IMemoryCache cache, TimeProvider clock, ILogger<LiveCheck> log)
{
    public const int MaxObjects = 30;
    public const int MaxResponseBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FoundLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan NotFoundLifetime = TimeSpan.FromMinutes(2);

    private readonly DreamClient dream = new(http)
    {
        MaxAttempts = 2,
        Pause = TimeSpan.Zero,
        FirstRetryDelay = TimeSpan.FromMilliseconds(500),
    };

    [GeneratedRegex("^[0-9a-z-]{36}$")]
    private static partial Regex ObjectIdPattern();

    /// <summary>Налаштування HttpClient для DREAM: адреса, заголовки, тайм-аут, обмеження розміру.</summary>
    public static void Configure(HttpClient client)
    {
        client.BaseAddress = new Uri(DreamClient.BaseAddress);
        client.Timeout = RequestTimeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DreamClient.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public LiveOutcome? Cached(string code) => cache.Get<LiveOutcome>(CacheKey(code));

    /// <param name="code">Код без "DREAM-UA-", уже перевірений <see cref="CodeInput"/>.</param>
    public async Task<LiveOutcome> RunAsync(string code, CancellationToken ct)
    {
        if (Cached(code) is { } cached) return cached;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TotalTimeout);

        LiveOutcome outcome;
        try
        {
            outcome = await FetchAsync(code, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // тип помилки без тексту: у тексті може бути адреса з кодом, а коди в журнал не пишемо
            log.LogWarning("Жива перевірка не вдалася: {Error}", ex.GetType().Name);
            return new LiveOutcome(LiveStatus.Unavailable);
        }

        if (outcome.Status is LiveStatus.Found or LiveStatus.NotFound)
        {
            var lifetime = outcome.Status == LiveStatus.Found ? FoundLifetime : NotFoundLifetime;
            cache.Set(CacheKey(code), outcome, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = lifetime });
        }
        return outcome;
    }

    private async Task<LiveOutcome> FetchAsync(string code, CancellationToken ct)
    {
        string? json = await dream.GetProjectAsync(code, ct);
        if (json is null) return new LiveOutcome(LiveStatus.NotFound);

        var card = CardParser.ParseProject(json);
        if (!string.Equals(card.Code, "DREAM-UA-" + code, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DREAM повернув іншу картку");
        if (card.ObjectIds.Count > MaxObjects) return new LiveOutcome(LiveStatus.TooManyObjects);
        if (!card.ObjectIds.All(ObjectIdPattern().IsMatch))
            throw new InvalidDataException("Незвичний ідентифікатор об'єкта");

        var objects = new Dictionary<string, ObjectCard>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            card.ObjectIds.Distinct(StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
            async (id, token) =>
            {
                if (await dream.GetObjectAsync(id, token) is not { } objectJson) return;
                var parsed = CardParser.ParseObject(objectJson);
                lock (objects) objects[parsed.Id] = parsed;
            });

        var now = clock.GetUtcNow();
        var facts = ProjectFacts.Of(card, objects, now);
        var result = CheckList.Evaluate(facts);

        return new LiveOutcome(LiveStatus.Found, new LiveResult(
            code,
            card.Title,
            Organisations.TitleOf(card.Organisation),
            card.Organisation?.Edrpou,
            Regions.Title(result.RegionKey),
            card.Stage.Title(),
            card.IsOpen,
            CheckList.All.Where((_, i) => result.IsFilled(i)).ToList(),
            result.Missing.ToList(),
            result.ScoreApplicable,
            result.ScoreFilled,
            result.MissingObjects,
            now));
    }

    private static string CacheKey(string code) => "live:" + code;
}
