using DreamPovnota.Digest;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using Microsoft.Net.Http.Headers;

namespace DreamPovnota.Web.Lists;

/// <summary>Зріз однієї організації і його файли.</summary>
public sealed record DigestEntry(OrganisationDigest Digest, DigestFile Xlsx, DigestFile Pdf);

public sealed record DigestFile(byte[] Content, string ContentType, string DownloadName, EntityTagHeaderValue ETag, DateTimeOffset LastModified);

/// <param name="Baseline">Стан на початок тижня; null, якщо історії не було.</param>
public sealed record DigestSet(IReadOnlyList<DigestEntry> Entries, DigestBaseline? Baseline, DateTimeOffset BuiltAt)
{
    public DigestEntry? Find(string edrpou) => Entries.FirstOrDefault(e => e.Digest.Organisation.Edrpou == edrpou);
}

/// <summary>
/// Зрізи для агентства й служб відновлення, зібрані раз на випуск, як <see cref="ExportFiles"/>.
/// Файли малі (разом близько мегабайта), тож лежать у пам'яті. Від нових знімків історії
/// зріз не залежить: стан на початок тижня в історії вже не змінюється. Виняток один:
/// якщо під час збірки база не відповіла, через хвилину зріз збирається знову.
/// </summary>
public sealed class DigestFiles(HistoryReader history, TimeProvider clock, ILogger<DigestFiles> log)
{
    public static readonly TimeSpan RetryWithoutHistory = TimeSpan.FromMinutes(1);

    private static readonly IReadOnlyList<string> Codes = DigestOrganisations.All.Select(o => o.Edrpou).ToList();

    private readonly Lock gate = new();
    private LoadedEdition? edition;
    private Task<DigestSet>? set;

    /// <summary>Почати збирати зрізи нового випуску у фоні.</summary>
    public void Prepare(LoadedEdition loaded) => _ = For(loaded);

    public Task<DigestSet> GetAsync(LoadedEdition loaded, CancellationToken ct) => For(loaded).WaitAsync(ct);

    private Task<DigestSet> For(LoadedEdition loaded)
    {
        lock (gate)
        {
            if (ReferenceEquals(edition, loaded) && set is { IsFaulted: false, IsCanceled: false } && !WorthRetrying(set))
                return set;
            edition = loaded;
            set = Task.Run(() => BuildAsync(loaded));
            return set;
        }
    }

    private bool WorthRetrying(Task<DigestSet> built) =>
        built.IsCompletedSuccessfully
        && built.Result.Baseline is null
        && history.IsConfigured
        && clock.GetUtcNow() - built.Result.BuiltAt > RetryWithoutHistory;

    private async Task<DigestSet> BuildAsync(LoadedEdition loaded)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var baseline = await history.DigestBaselineAsync(Codes, DigestBuilder.BaselineAt(loaded.Edition), CancellationToken.None);
        string prefix = Convert.ToHexStringLower(loaded.ContentHash.AsSpan(0, 8)) + (baseline is null ? "-n" : "-h");

        var entries = new List<DigestEntry>(DigestOrganisations.All.Count);
        foreach (var organisation in DigestOrganisations.All)
        {
            var digest = DigestBuilder.Build(loaded.Edition, loaded.Sha256, organisation, baseline);
            using var xlsx = new MemoryStream();
            await DigestWorkbook.WriteAsync(xlsx, digest, CancellationToken.None);
            entries.Add(new DigestEntry(
                digest,
                File(xlsx.ToArray(), DigestWorkbook.ContentType, digest, "xlsx", prefix),
                File(DigestPdf.Write(digest), DigestPdf.ContentType, digest, "pdf", prefix)));
        }

        // як і після повних вивантажень: разове сміття збірки не має лишатися в 512 МБ екземпляра
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        log.LogInformation(
            "Зрізи для служб відновлення зібрано за {Elapsed:0.0} с, {Bytes} КБ, історія {History}",
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds,
            entries.Sum(e => e.Xlsx.Content.Length + e.Pdf.Content.Length) / 1024,
            baseline is null ? "недоступна" : "є");
        return new DigestSet(entries, baseline, clock.GetUtcNow());
    }

    private static DigestFile File(byte[] content, string type, OrganisationDigest digest, string extension, string prefix) =>
        new(content, type, digest.FileName(extension),
            new EntityTagHeaderValue($"\"{prefix}-{digest.Organisation.Edrpou}-{extension}\""), digest.Generated);
}
