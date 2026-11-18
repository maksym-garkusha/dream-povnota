using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Обмеження частоти на клієнта. Спільні на весь сервіс межі для живих перевірок і збірки
/// вивантажень бере сам обробник (<see cref="SharedLimits"/>), бо їх має витрачати тільки
/// справжня робота. Спільні межі тримають навантаження навіть тоді, коли адресу клієнта
/// за проксі Render визначено неточно.
/// </summary>
public static class Limits
{
    public const int ChecksPerMinute = 6;
    public const int ChecksPerHour = 30;
    public const int ChecksPerHourOverall = 120;
    public const int ChecksAtOnce = 2;
    public const int ChecksQueued = 10;
    public const int RequestsPerMinute = 120;

    /// <summary>
    /// Усі оновлення бота приходять з кількох адрес Telegram, тож звичайний ліміт сторінок
    /// зупинив би всіх користувачів разом. Цей вищий; у бота ще свої ліміти на чат.
    /// </summary>
    public const int TelegramPerMinute = 300;
    public const int ExportsPerMinute = 10;
    public const int ExportBuildsPerMinute = 30;
    public const int ExportBuildsQueued = 4;

    private enum Kind { None, Page, Export, Check, Telegram }

    private static Kind KindOf(HttpRequest request)
    {
        var path = request.Path;
        // /health/ready ходить у базу, тож його ліміт як у сторінки; без ліміту лише перевірка Render
        if (path.StartsWithSegments("/health/live") || path.StartsWithSegments("/status") || path.StartsWithSegments("/error"))
            return Kind.None;
        if (path.StartsWithSegments(Telegram.TelegramBot.Path)) return Kind.Telegram;
        if (HttpMethods.IsPost(request.Method) && path.StartsWithSegments("/check")) return Kind.Check;
        if (path.Value is { } value
            && (value.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            return Kind.Export;
        return Kind.Page;
    }

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(PerClient(), ChecksPerClientHour());
        options.OnRejected = OnRejectedAsync;
    }

    private static PartitionedRateLimiter<HttpContext> PerClient() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) switch
        {
            Kind.None => RateLimitPartition.GetNoLimiter("none"),
            Kind.Check => Window("check:" + ClientKey.Of(context), ChecksPerMinute, TimeSpan.FromMinutes(1)),
            Kind.Export => Window("export:" + ClientKey.Of(context), ExportsPerMinute, TimeSpan.FromMinutes(1)),
            Kind.Telegram => Window("telegram:" + ClientKey.Of(context), TelegramPerMinute, TimeSpan.FromMinutes(1)),
            _ => Window("page:" + ClientKey.Of(context), RequestsPerMinute, TimeSpan.FromMinutes(1)),
        });

    private static PartitionedRateLimiter<HttpContext> ChecksPerClientHour() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => KindOf(context.Request) == Kind.Check
            ? Window("check-hour:" + ClientKey.Of(context), ChecksPerHour, TimeSpan.FromHours(1))
            : RateLimitPartition.GetNoLimiter("none"));

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
