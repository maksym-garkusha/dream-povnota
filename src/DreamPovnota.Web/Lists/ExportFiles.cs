using System.IO.Compression;
using DreamPovnota.Web.Import;
using Microsoft.Net.Http.Headers;

namespace DreamPovnota.Web.Lists;

/// <summary>Готовий файл вивантаження на диску.</summary>
public sealed record ExportFile(string Path, string ContentType, string DownloadName, EntityTagHeaderValue ETag, DateTimeOffset LastModified);

/// <param name="CsvGzip">Той самий CSV, уже стиснений, для клієнтів з Accept-Encoding: gzip.</param>
public sealed record ExportFileSet(ExportFile Csv, ExportFile CsvGzip, ExportFile Xlsx);

/// <summary>
/// Повні вивантаження без фільтра. Їх просять найчастіше, а збирати повний xlsx на 0,1 CPU
/// довго, тому файли готуються один раз на випуск, одразу після імпорту, і далі віддаються з диска
/// з ETag і докачуванням. На диску, а не в пам'яті: разом це близько 17 МБ, а пам'яті 512 МБ.
/// </summary>
public sealed class ExportFiles(ILogger<ExportFiles> log) : IDisposable
{
    // своя тека на кожен запуск: два процеси (або два тестові сервери) не видаляють файлів один одного
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dream-povnota-exports-" + Guid.NewGuid().ToString("N"));
    private readonly Lock gate = new();
    private LoadedEdition? edition;
    private Task<ExportFileSet>? files;
    private string? previousPrefix;

    /// <summary>Почати збирати файли нового випуску у фоні.</summary>
    public void Prepare(LoadedEdition loaded) => _ = For(loaded);

    /// <summary>Файли випуску; якщо їх ще збирають, дочекатися (запити не збирають файли вдруге).</summary>
    public Task<ExportFileSet> GetAsync(LoadedEdition loaded, CancellationToken ct) => For(loaded).WaitAsync(ct);

    private Task<ExportFileSet> For(LoadedEdition loaded)
    {
        lock (gate)
        {
            if (ReferenceEquals(edition, loaded) && files is { IsFaulted: false, IsCanceled: false }) return files;
            edition = loaded;
            files = Task.Run(() => BuildAsync(loaded));
            return files;
        }
    }

    private async Task<ExportFileSet> BuildAsync(LoadedEdition loaded)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Directory.CreateDirectory(directory);
        string prefix = Convert.ToHexStringLower(loaded.ContentHash.AsSpan(0, 8));
        RemoveOld(prefix);

        var all = Enumerable.Range(0, loaded.Edition.Cards.Count).ToList();
        var headers = ExportRows.Headers(loaded);

        string csv = await WriteAsync(prefix + ".csv", stream => CsvExport.WriteAsync(stream, headers, ExportRows.Rows(loaded, all), CancellationToken.None));
        string csvGzip = await WriteAsync(prefix + ".csv.gz", async stream =>
        {
            await using var source = File.OpenRead(csv);
            await using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            await source.CopyToAsync(gzip);
        });
        string xlsx = await WriteAsync(prefix + ".xlsx", stream => XlsxExport.WriteAsync(stream, headers, ExportRows.Rows(loaded, all), CancellationToken.None));

        // збірка разово займає понад сотню мегабайтів; без цього процес тримав би їх до кінця життя,
        // а на Render усього 512 МБ
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        log.LogInformation("Повні вивантаження зібрано за {Elapsed:0.0} с", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds);
        var generated = loaded.Edition.Generated;
        return new ExportFileSet(
            new ExportFile(csv, CsvExport.ContentType, ExportRows.FileName(loaded, "csv"), Tag(prefix, "csv"), generated),
            new ExportFile(csvGzip, CsvExport.ContentType, ExportRows.FileName(loaded, "csv"), Tag(prefix, "csv-gzip"), generated),
            new ExportFile(xlsx, XlsxExport.ContentType, ExportRows.FileName(loaded, "xlsx"), Tag(prefix, "xlsx"), generated));
    }

    // спершу в тимчасове ім'я, потім перейменування: хто саме качає старий файл, докачає його цілим
    private async Task<string> WriteAsync(string name, Func<Stream, Task> write)
    {
        string path = Path.Combine(directory, name);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        {
            await write(file);
        }
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    // файли попереднього випуску лишаємо: їх ще можуть докачувати
    private void RemoveOld(string prefix)
    {
        string? keep;
        lock (gate)
        {
            keep = previousPrefix == prefix ? null : previousPrefix;
            previousPrefix = prefix;
        }
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith(prefix, StringComparison.Ordinal) || (keep is not null && name.StartsWith(keep, StringComparison.Ordinal))) continue;
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                log.LogWarning(ex, "Старий файл вивантаження не видалено");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Теку вивантажень не видалено");
        }
    }

    private static EntityTagHeaderValue Tag(string prefix, string format) => new($"\"{prefix}-{format}\"");
}
