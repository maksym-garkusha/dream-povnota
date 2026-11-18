using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Заголовки безпеки й кешування для всіх відповідей. Політика CSP сувора:
/// ні вбудованих скриптів, ні вбудованих стилів, бо Razor Pages їх не потребують.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";

    public const string PublicCache = "public, max-age=300";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context);
                return Task.CompletedTask;
            });
            await next(context);
        });

    private static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path;

        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers.XFrameOptions = "DENY";

        // відкриті дані без облікових записів: читати API можна з будь-якого сайту
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/openapi"))
            headers.AccessControlAllowOrigin = "*";

        if (IsPrivate(path) || context.Response.StatusCode >= 400)
        {
            headers.CacheControl = "no-store";
            return;
        }

        // сторінки з токеном форми антифорджері вже позначило no-store, їх не чіпаємо
        if (headers.CacheControl.Count == 0)
        {
            headers.CacheControl = PublicCache;
            if (context.RequestServices.GetRequiredService<EditionStore>().Current is { } edition)
                headers.LastModified = edition.Edition.Generated.ToString("R");
        }
    }

    private static bool IsPrivate(PathString path) =>
        path.StartsWithSegments("/check") || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/status") || path.StartsWithSegments("/error")
        || path.StartsWithSegments(Telegram.TelegramBot.Path);
}
