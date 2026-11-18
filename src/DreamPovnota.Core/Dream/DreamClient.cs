using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DreamPovnota.Cards;

namespace DreamPovnota.Dream;

/// <summary>Запис стрічки: який проєкт чи об'єкт і коли він змінювався востаннє.</summary>
public record FeedEntry(string Id, string? Code, DateTimeOffset Updated);

/// <summary>
/// Клієнт до публічного API DREAM. Стрічки /ideas і /objects віддають до 1000
/// записів за раз, упорядкованих за датою зміни; наступну сторінку просимо
/// через ?from= з датою останнього запису.
/// </summary>
public sealed class DreamClient(HttpClient http)
{
    public const string BaseAddress = "https://public-api.dream.gov.ua/marketplace/public/dream/";
    public const string UserAgent = "dream-povnota/1.0 (+https://github.com/maksym-garkusha/dream-povnota)";

    /// <summary>Скільки разів пробуємо один запит, разом з першим.</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Сторінка, коротша за цю, означає кінець стрічки.</summary>
    public int PageSize { get; init; } = 1000;

    /// <summary>Пауза перед другою спробою; далі подвоюється: 2, 4, 8 с.</summary>
    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Пауза перед кожним запитом. Кожен потік синхронізації просить картки по одній,
    /// тож це пауза між його запитами: API державний і публічний, поспішати нам нікуди.
    /// </summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(100);

    // сервер може попросити зачекати довше, але годину ми не чекатимемо
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>HttpClient з усім, без чого API не відповідає. Один на застосунок.</summary>
    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = new Uri(BaseAddress);
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public IAsyncEnumerable<FeedEntry> ProjectFeedAsync(CancellationToken ct = default) => FeedAsync("ideas", ct);

    public IAsyncEnumerable<FeedEntry> ObjectFeedAsync(CancellationToken ct = default) => FeedAsync("objects", ct);

    /// <summary>Сира відповідь /ideas/{id} або null, якщо проєкт зник (404).</summary>
    public Task<string?> GetProjectAsync(string id, CancellationToken ct = default) =>
        GetAsync($"ideas/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Сира відповідь /objects/{id} або null, якщо об'єкт зник (404).</summary>
    public Task<string?> GetObjectAsync(string id, CancellationToken ct = default) =>
        GetAsync($"objects/{Uri.EscapeDataString(id)}", ct);

    private async IAsyncEnumerable<FeedEntry> FeedAsync(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        string? from = null;
        while (true)
        {
            string uri = from is null ? path : $"{path}?from={Uri.EscapeDataString(from)}";
            string json = await GetAsync(uri, ct)
                ?? throw new HttpRequestException($"Стрічка {uri} відповіла 404", null, HttpStatusCode.NotFound);

            var page = ReadFeedPage(json);
            foreach (var entry in page.Entries)
                yield return entry;

            // "from" беремо рядком як є, щоб не загубити мікросекунди
            if (page.Size < PageSize || page.LastUpdated is null || page.LastUpdated == from)
                yield break;
            from = page.LastUpdated;
        }
    }

    /// <param name="Size">Скільки записів було на сторінці, разом із тими, що не вдалося прочитати.</param>
    private record FeedPage(List<FeedEntry> Entries, int Size, string? LastUpdated);

    private static FeedPage ReadFeedPage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.Prop("data").Items().ToList();
        var entries = new List<FeedEntry>(items.Count);
        string? last = null;

        foreach (var item in items)
        {
            string? updated = item.Prop("external").Prop("updated").Text();
            string? id = item.Prop("internal").Prop("id").Text();
            if (updated is not null) last = updated;

            if (id is not null && DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, out var date))
                entries.Add(new FeedEntry(id, item.Prop("internal").Prop("code").Text(), date));
        }

        return new FeedPage(entries, items.Count, last);
    }

    /// <summary>
    /// GET з повторами на мережеві збої, тайм-аут, 5xx і 429. Повертає null на 404,
    /// на інші коди помилки кидає <see cref="HttpRequestException"/> одразу.
    /// </summary>
    private async Task<string?> GetAsync(string uri, CancellationToken ct)
    {
        if (Pause > TimeSpan.Zero) await Task.Delay(Pause, ct);

        for (int attempt = 1; ; attempt++)
        {
            TimeSpan? wait = null;
            try
            {
                using var response = await http.GetAsync(uri, ct);

                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(ct);
                if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                    throw new HttpRequestException(
                        $"{uri}: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);

                wait = RetryAfter(response);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct) && attempt < MaxAttempts)
            {
                // мережевий збій або тайм-аут: пробуємо ще
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new HttpRequestException($"{uri}: немає відповіді за {http.Timeout.TotalSeconds} с", ex);
            }

            await Task.Delay(wait ?? FirstRetryDelay * Math.Pow(2, attempt - 1), ct);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    // HttpRequestException без коду це обрив з'єднання; з кодом її кидаємо ми самі вище
    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        IOException => true,
        TaskCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? wait = header?.Delta
            ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (wait is not { } w || w <= TimeSpan.Zero) return null;
        return w < MaxRetryAfter ? w : MaxRetryAfter;
    }
}
