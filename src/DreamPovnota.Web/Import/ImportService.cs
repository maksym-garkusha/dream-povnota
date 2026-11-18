using System.Diagnostics;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Import;

/// <summary>
/// Забирає data.json одразу після старту і далі щогодини, поки екземпляр не спить.
/// Після збою пробує частіше (10 с, 30 с, 1, 2, 5 хв, далі кожні 10 хв): поки випуску
/// немає, усі сторінки відповідають 503, і чекати годину через один обірваний запит не можна.
/// Новий випуск спершу проходить <see cref="EditionGuard"/>: якщо в ньому за раз змінилось
/// забагато, поточним лишається попередній. Прийнятий випуск одразу стає поточним; кеш
/// сторінок, зібраних зі старого, скидається, а повні вивантаження нового починають збиратися у фоні.
/// Потім, якщо є база, випуск записується знімком історії, і після нього збирається зріз
/// для служб відновлення. Збій бази не заважає
/// поточному стану: знімок допишеться наступного разу.
/// </summary>
public sealed class ImportService(
    EditionSource source,
    EditionStore store,
    EditionGuard guard,
    Db db,
    DbKeyRepository keys,
    ExportFiles exports,
    DigestFiles digests,
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
            else if (await guard.CheckAsync(edition, store.Current, ct) is { } jump)
            {
                // поточним лишається попередній, якщо він є; числа тільки в журналі,
                // назовні в /health/ready лише тип причини
                store.Held(clock.GetUtcNow(), jump.Reason);
                log.LogWarning(
                    "Випуск від {Generated:u} ({Sha256}) не прийнято: {Jump}",
                    edition.Edition.Generated, edition.Sha256, jump);
            }
            else
            {
                store.Publish(edition, clock.GetUtcNow());
                await outputCache.EvictByTagAsync(CacheTag, ct);
                exports.Prepare(edition);
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
        // зріз читає історію, тож збирається вже після знімка; якщо його вже зібрано, нічого не робить
        if (store.Current is { } current) digests.Prepare(current);
        return fetched && store.Current is not null;
    }

    /// <summary>
    /// Тип збою без тексту, для /health/ready: «HttpRequestException: 404», «HttpRequestException → IOException».
    /// Текст винятку назовні не віддаємо, бо в ньому бувають адреси; повністю він є в журналі.
    /// </summary>
    public static string ShortReason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        string kind = ex is HttpRequestException { StatusCode: { } code } ? $"{ex.GetType().Name}: {(int)code}" : ex.GetType().Name;
        return inner == ex ? kind : $"{kind} → {inner.GetType().Name}";
    }

    private async Task RecordAsync(CancellationToken ct)
    {
        if (!db.IsAvailable) return;
        var current = store.Current;
        bool snapshotDue = current is not null && current.Edition.Generated != recorded;
        if (!snapshotDue && !keys.HasUnsaved) return;

        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!migrated)
            {
                int applied = await Migrator.MigrateAsync(db, ct);
                if (applied > 0) log.LogInformation("Застосовано міграцій бази: {Count}", applied);
                migrated = true;
            }

            // ключі форм, створені, поки бази не було чи вона не відповідала: пробуємо щоразу, доки не збережуться
            if (keys.HasUnsaved) await keys.SaveUnsavedAsync(ct);
            if (!snapshotDue || current is null) return;

            int? snapshot = await SnapshotWriter.WriteAsync(db, current, ct);
            recorded = current.Edition.Generated;
            db.MarkUp();
            if (snapshot is null) return;

            // транзакцію знімка вже закомічено: спершу нове покоління ключа кешу, потім очищення
            store.HistoryChanged();
            await outputCache.EvictByTagAsync(CacheTag, ct);
            log.LogInformation("Знімок {Snapshot} записано за {Elapsed:0.0} с", snapshot, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        catch (Exception ex) when (Db.IsOutage(ex) && !ct.IsCancellationRequested)
        {
            db.MarkDown();
            log.LogWarning(ex, "Знімок або ключі форм не записано, база недоступна");
        }
    }
}
