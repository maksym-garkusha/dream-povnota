using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DreamPovnota.Digest;
using DreamPovnota.Dream;
using DreamPovnota.Export;

namespace DreamPovnota.Report;

/// <summary>
/// Щотижневий зріз з опублікованого data.json: xlsx і PDF на кожну організацію з
/// <see cref="DigestOrganisations"/>. Стан на початок тижня береться з веб-сервісу;
/// якщо він не відповів, файли все одно збираються, з поясненням, що історії немає.
/// </summary>
public static class DigestCommand
{
    public static async Task<int> RunAsync(string editionPath, string? service, string directory, TextWriter log, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        Edition edition;
        string sha256;
        try
        {
            // хеш рахується з тих самих байтів, що й розбір, а не з окремого читання файлу
            byte[] body = await File.ReadAllBytesAsync(editionPath, ct);
            sha256 = Convert.ToHexStringLower(SHA256.HashData(body));
            edition = await Edition.ReadAsync(new MemoryStream(body, writable: false), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"data.json не прочитано: {ex.Message}");
            return 1;
        }
        log.WriteLine($"Випуск від {edition.Generated:u}, SHA-256 {sha256}, 7 днів {DigestText.Period(DigestBuilder.WeekFrom(edition), DigestBuilder.WeekTo(edition))}");

        foreach (var (edrpou, title) in DigestOrganisations.Unlisted(edition.Organisations.Select(o => (o.Edrpou, o.Title))))
            log.WriteLine($"Схожа організація, якої немає в переліку: {edrpou ?? "без ЄДРПОУ"} {title}");

        var baseline = service is null ? null : await BaselineAsync(service, DigestBuilder.WeekFrom(edition), log, ct);

        Directory.CreateDirectory(directory);
        long bytes = 0;
        int files = 0;
        foreach (var organisation in DigestOrganisations.All)
        {
            var digest = DigestBuilder.Build(edition, sha256, organisation, baseline);

            string xlsx = Path.Combine(directory, digest.FileName("xlsx"));
            await using (var file = File.Create(xlsx)) await DigestWorkbook.WriteAsync(file, digest, ct);
            string pdf = Path.Combine(directory, digest.FileName("pdf"));
            await File.WriteAllBytesAsync(pdf, DigestPdf.Write(digest), ct);

            bytes += new FileInfo(xlsx).Length + new FileInfo(pdf).Length;
            files += 2;
            log.WriteLine(
                $"{organisation.Edrpou}: відкритих {digest.Open}, заповнено все доречне {digest.Complete}, " +
                $"оновлено за 7 днів {digest.UpdatedThisWeek}, змін у таблиці {digest.Week.Count}");
        }

        log.WriteLine(
            $"Файлів {files}, разом {bytes / 1024} КБ, у {Path.GetFullPath(directory)}; " +
            $"час {clock.Elapsed.TotalSeconds:0.0} с, пікова пам'ять {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} МБ");
        return 0;
    }

    private static async Task<DigestBaseline?> BaselineAsync(string service, DateOnly from, TextWriter log, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = DigestHistoryClient.Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(DreamClient.UserAgent);
        var client = new DigestHistoryClient(http);

        // друга спроба на випадок, коли перший запит тільки розбудив сервіс
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var baseline = await client.FetchAsync(service, from, ct);
                log.WriteLine($"Історія з {baseline.HistoryFrom:u}, порівняння зі знімком від {baseline.Taken:u}, карток у ньому {baseline.Cards.Count}");
                return baseline;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                    continue;
                }
                log.WriteLine($"Історія недоступна ({ex.GetType().Name}): у файлах тільки дати оновлення з DREAM");
                return null;
            }
        }
    }
}
