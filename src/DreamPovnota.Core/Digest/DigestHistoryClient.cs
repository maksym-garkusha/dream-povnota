using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace DreamPovnota.Digest;

/// <summary>
/// Стан карток на початок тижня з веб-сервісу: /api/v1/digest/baseline. Історія живе
/// в його базі, тож CLI бере її звідти, а не тримає копію.
/// </summary>
public sealed class DigestHistoryClient(HttpClient http)
{
    public const string Path = "/api/v1/digest/baseline";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Безкоштовний екземпляр спить і прокидається близько хвилини.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public static string Query(DateOnly from) => Path + "?from=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <param name="service">Адреса сервісу без шляху, наприклад <see cref="DigestBuilder.ServiceUrl"/>.</param>
    /// <exception cref="HttpRequestException">Сервіс не відповів або історії в ньому немає (503).</exception>
    public async Task<DigestBaseline> FetchAsync(string service, DateOnly from, CancellationToken ct)
    {
        var uri = new Uri(new Uri(service.TrimEnd('/') + "/"), Query(from).TrimStart('/'));
        var baseline = await http.GetFromJsonAsync<DigestBaseline>(uri, Json, ct)
            ?? throw new InvalidDataException("Порожня відповідь замість стану карток");
        if (baseline.Taken < baseline.HistoryFrom)
            throw new InvalidDataException("Знімок для порівняння старший за початок історії");
        return baseline;
    }
}
