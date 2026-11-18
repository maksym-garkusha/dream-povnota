using System.Diagnostics;
using DreamPovnota.Web.History;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Import;

/// <summary>
/// Забирає data.json одразу після старту і далі щогодини, поки екземпляр не спить.
/// Після збою пробує частіше (10 с, 30 с, 1, 2, 5 хв, далі кожні 10 хв): поки випуску
/// немає, усі сторінки відповідають 503, і чекати годину через один обірваний запит не можна.
/// Новий випуск одразу стає поточним; кеш сторінок, зібраних зі старого, скидається.
/// Потім, якщо є база, випуск записується знімком історії. Збій бази не заважає
/// поточному стану: знімок допишеться наступного разу.
/// </summary>
public sealed class ImportService(
    EditionSource source,
    EditionStore store,
    Db db,
    DbKeyRepository keys,
    IOutputCacheStore outputCache,
    TimeProvider clock,
    ILogger<ImportService> log) : BackgroundService
{
    public const string CacheTag = "edition";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
    ];

    private bool migrated;
    private DateTimeOffset? recorded;

    /// <summary>Пауза після <paramref name="failures"/> невдалих спроб поспіль; 0 означає успіх.</summary>
    public static TimeSpan DelayAfter(int failures) =>
        failures == 0 ? Interval : RetryDelays[Math.Min(failures, RetryDelays.Count) - 1];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // старт застосунку не чекає на завантаження: Kestrel має слухати якнайшвидше
        await Task.Yield();

        int failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            failures = await ImportOnceAsync(stoppingToken) ? 0 : failures + 1;
            try
            {
                await Task.Delay(DelayAfter(failures), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <returns>true, якщо джерело відповіло (новим випуском або 304) і поточний випуск є.</returns>
    public async Task<bool> ImportOnceAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        bool fetched;
        try
        {
            var edition = await source.FetchAsync(ct);
            if (edition is null)
            {
                store.Checked(clock.GetUtcNow());
            }
            else
            {
                store.Publish(edition, clock.GetUtcNow());
                await outputCache.EvictByTagAsync(CacheTag, ct);
                log.LogInformation(
                    "Випуск від {Generated:u}: {Cards} карток, завантажено за {Elapsed:0.0} с",
                    edition.Edition.Generated, edition.Edition.Cards.Count, Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            fetched = true;
        }
        // TaskCanceledException без скасування ззовні це тайм-аут HttpClient
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // попередній випуск лишається поточним, а причину видно в /health/ready
            store.Failed(clock.GetUtcNow(), ShortReason(ex));
            log.LogWarning(ex, "Випуск даних не завантажено");
            fetched = false;
        }

        await RecordAsync(ct);
        return fetched && store.Current is not null;
    }

    /// <summary>Повідомлення винятку і найглибшої причини: «Error while copying… → The response ended prematurely.»</summary>
    public static string ShortReason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        return inner == ex || inner.Message == ex.Message ? ex.Message : $"{ex.Message} → {inner.Message}";
    }

    private async Task RecordAsync(CancellationToken ct)
    {
        if (store.Current is not { } current || current.Edition.Generated == recorded || !db.IsAvailable) return;

        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!migrated)
            {
                int applied = await Migrator.MigrateAsync(db, ct);
                if (applied > 0) log.LogInformation("Застосовано міграцій бази: {Count}", applied);
                migrated = true;
                await keys.SaveUnsavedAsync(ct);
            }

            int? snapshot = await SnapshotWriter.WriteAsync(db, current, ct);
            recorded = current.Edition.Generated;
            db.MarkUp();
            if (snapshot is null) return;

            await outputCache.EvictByTagAsync(CacheTag, ct);
            log.LogInformation("Знімок {Snapshot} записано за {Elapsed:0.0} с", snapshot, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        catch (Exception ex) when (Db.IsOutage(ex) && !ct.IsCancellationRequested)
        {
            db.MarkDown();
            log.LogWarning(ex, "Знімок не записано, база недоступна");
        }
    }
}
