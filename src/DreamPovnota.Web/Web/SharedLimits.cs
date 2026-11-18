using System.Threading.RateLimiting;

namespace DreamPovnota.Web.Web;

/// <summary>Дозвіл на дорогу дію або відмова з часом, через який варто спробувати знову.</summary>
public sealed class Permit(RateLimitLease? lease, TimeSpan retryAfter) : IDisposable
{
    public bool Granted => lease is not null;

    public TimeSpan RetryAfter => retryAfter;

    public void Dispose() => lease?.Dispose();
}

/// <summary>
/// Спільні на весь сервіс межі для дорогої роботи. Їх бере сам обробник, а не middleware,
/// тож межу витрачає тільки справжня робота: збірка файлу після промаху кешу. Відповідь
/// з кешу чи відхилений запит її не з'їдають, і кількома дешевими запитами її не вичерпати.
/// </summary>
public sealed class SharedLimits : IDisposable
{
    private readonly TokenBucketRateLimiter exportRate;
    private readonly ConcurrencyLimiter exportAtOnce;

    public SharedLimits()
        : this(Limits.ExportBuildsPerMinute)
    {
    }

    public SharedLimits(int exportBuildsPerMinute)
    {
        exportRate = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = exportBuildsPerMinute,
            TokensPerPeriod = exportBuildsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
        // на 0,1 CPU дві збірки одночасно не швидші за дві підряд, тож по одній
        exportAtOnce = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = Limits.ExportBuildsQueued,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>Зібрати вивантаження з фільтром, якого немає в кеші.</summary>
    public Task<Permit> ExportBuildAsync(CancellationToken ct) => AcquireAsync(exportRate, exportAtOnce, ct);

    private static async Task<Permit> AcquireAsync(RateLimiter rate, RateLimiter atOnce, CancellationToken ct)
    {
        var token = rate.AttemptAcquire();
        if (!token.IsAcquired)
        {
            var wait = token.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : TimeSpan.FromMinutes(1);
            token.Dispose();
            return new Permit(null, wait);
        }
        token.Dispose();

        var slot = await atOnce.AcquireAsync(1, ct);
        if (slot.IsAcquired) return new Permit(slot, TimeSpan.Zero);
        slot.Dispose();
        return new Permit(null, TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        exportRate.Dispose();
        exportAtOnce.Dispose();
    }
}
