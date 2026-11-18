using System.Diagnostics;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Import;

/// <summary>
/// Забирає data.json одразу після старту і далі щогодини, поки екземпляр не спить.
/// Новий випуск одразу стає поточним; кеш сторінок, зібраних зі старого, скидається.
/// </summary>
public sealed class ImportService(
    EditionSource source,
    EditionStore store,
    IOutputCacheStore outputCache,
    TimeProvider clock,
    ILogger<ImportService> log) : BackgroundService
{
    public const string CacheTag = "edition";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // старт застосунку не чекає на завантаження: Kestrel має слухати якнайшвидше
        await Task.Yield();

        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await ImportOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task ImportOnceAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var edition = await source.FetchAsync(ct);
            if (edition is null)
            {
                store.Checked(clock.GetUtcNow());
                return;
            }

            store.Publish(edition, clock.GetUtcNow());
            await outputCache.EvictByTagAsync(CacheTag, ct);
            log.LogInformation(
                "Випуск від {Generated:u}: {Cards} карток, завантажено за {Elapsed:0.0} с",
                edition.Edition.Generated, edition.Edition.Cards.Count, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        // TaskCanceledException без скасування ззовні це тайм-аут HttpClient
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // попередній випуск лишається поточним, а причину видно в /health/ready
            store.Failed(clock.GetUtcNow(), ex.Message);
            log.LogWarning("Випуск даних не завантажено: {Error}", ex.Message);
        }
    }
}
