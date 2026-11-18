using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Обмеження частоти. Окрім лімітів на клієнта є спільні: на живі перевірки
/// (вони йдуть у DREAM) і на xlsx (найважче для 512 МБ). Спільні ліміти тримають
/// навантаження навіть тоді, коли адресу клієнта за проксі Render визначено неточно.
/// </summary>
public static class Limits
{
    public const int ChecksPerMinute = 6;
    public const int ChecksPerHour = 30;
    public const int ChecksPerHourOverall = 120;
    public const int ChecksAtOnce = 2;
    public const int ChecksQueued = 10;
    public const int RequestsPerMinute = 120;
    public const int ExportsPerMinute = 10;

    private enum Kind { None, Page, Export, Xlsx, Check }

    private static Kind KindOf(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/status") || path.StartsWithSegments("/error"))
            return Kind.None;
        if (HttpMethods.IsPost(request.Method) && path.StartsWithSegments("/check")) return Kind.Check;
        if (path.Value is { } value && value.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return Kind.Xlsx;
        if (path.Value is { } csv && csv.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return Kind.Export;
        return Kind.Page;
    }

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(PerClient(), ChecksPerClientHour(), Overall());
        options.OnRejected = OnRejectedAsync;
    }

    private static PartitionedRateLimiter<HttpContext> PerClient() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) switch
        {
            Kind.None => RateLimitPartition.GetNoLimiter("none"),
            Kind.Check => Window("check:" + ClientKey.Of(context), ChecksPerMinute, TimeSpan.FromMinutes(1)),
            Kind.Export or Kind.Xlsx => Window("export:" + ClientKey.Of(context), ExportsPerMinute, TimeSpan.FromMinutes(1)),
            _ => Window("page:" + ClientKey.Of(context), RequestsPerMinute, TimeSpan.FromMinutes(1)),
        });

    private static PartitionedRateLimiter<HttpContext> ChecksPerClientHour() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) == Kind.Check
            ? Window("check-hour:" + ClientKey.Of(context), ChecksPerHour, TimeSpan.FromHours(1))
            : RateLimitPartition.GetNoLimiter("none"));

    private static PartitionedRateLimiter<HttpContext> Overall() =>
        PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) == Kind.Check
                ? RateLimitPartition.GetTokenBucketLimiter("checks", _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = ChecksPerHourOverall,
                    TokensPerPeriod = ChecksPerHourOverall / 60,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter("none")),
            PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) switch
            {
                Kind.Check => RateLimitPartition.GetConcurrencyLimiter("checks-at-once", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = ChecksAtOnce,
                    QueueLimit = ChecksQueued,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }),
                Kind.Xlsx => RateLimitPartition.GetConcurrencyLimiter("xlsx-at-once", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 1,
                    QueueLimit = 2,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }),
                _ => RateLimitPartition.GetNoLimiter("none"),
            }));

    private static RateLimitPartition<string> Window(string key, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = window,
            QueueLimit = 0,
        });

    // тіла не пишемо: сторінку 429 покаже UseStatusCodePagesWithReExecute, а для API ProblemDetails
    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(60);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Забагато запитів",
                detail: "Спробуйте пізніше; скільки чекати, сказано в заголовку Retry-After.")
                .ExecuteAsync(context.HttpContext);
        }
    }
}
