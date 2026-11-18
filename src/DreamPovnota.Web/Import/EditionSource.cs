using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using DreamPovnota.Export;

namespace DreamPovnota.Web.Import;

/// <summary>
/// Звідки береться data.json: з GitHub Pages умовним GET, а для локального запуску
/// з файлу. Пам'ятає ETag і Last-Modified, тож незмінений файл коштує одну відповідь 304.
/// </summary>
public sealed class EditionSource(HttpClient http, IConfiguration configuration)
{
    public const string HttpClientName = "data";

    public const string DefaultUrl = "https://maksym-garkusha.github.io/dream-povnota/data.json";

    /// <summary>Справжній файл близько 5 МБ; більше за 20 МБ це вже не data.json.</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    private EntityTagHeaderValue? etag;
    private DateTimeOffset? lastModified;
    private DateTime? fileWritten;

    public string Location => configuration["DATA_URL"] is { Length: > 0 } url ? url : DefaultUrl;

    /// <summary>Новий випуск або null, якщо файл не змінився.</summary>
    /// <exception cref="InvalidDataException">Файл не пройшов перевірку.</exception>
    /// <exception cref="HttpRequestException">Джерело не відповіло як слід.</exception>
    public async Task<LoadedEdition?> FetchAsync(CancellationToken ct)
    {
        byte[]? body = IsWebAddress(Location) ? await DownloadAsync(ct) : await ReadFileAsync(ct);
        if (body is null) return null;

        var edition = await Edition.ReadAsync(new MemoryStream(body, writable: false), ct);
        return new LoadedEdition(edition, Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    private static bool IsWebAddress(string location) =>
        location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || location.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private async Task<byte[]?> DownloadAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Location);
        if (etag is not null) request.Headers.IfNoneMatch.Add(etag);
        if (lastModified is not null) request.Headers.IfModifiedSince = lastModified;

        // без ResponseHeadersRead тіло читається в буфер з обмеженням MaxResponseContentBufferSize
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified) return null;
        response.EnsureSuccessStatusCode();

        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);
        etag = response.Headers.ETag;
        lastModified = response.Content.Headers.LastModified;
        return body;
    }

    private async Task<byte[]?> ReadFileAsync(CancellationToken ct)
    {
        var file = new FileInfo(Path.GetFullPath(Location));
        if (!file.Exists) throw new InvalidDataException($"Немає файлу {file.Name}");
        if (file.Length > MaxBytes) throw new InvalidDataException($"Файл {file.Name} більший за {MaxBytes / 1024 / 1024} МБ");
        if (file.LastWriteTimeUtc == fileWritten) return null;

        byte[] body = await File.ReadAllBytesAsync(file.FullName, ct);
        fileWritten = file.LastWriteTimeUtc;
        return body;
    }
}
