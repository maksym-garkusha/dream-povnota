using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Поки перший випуск не завантажено, сторінки з даними відповідають 503 з Retry-After.
/// Тіло для браузера малює сторінка статусу, яка сама оновлюється.
/// </summary>
public static class EditionGate
{
    public const int RetryAfterSeconds = 5;

    public static IApplicationBuilder UseEditionGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (NeedsEdition(context.Request.Path)
                && context.RequestServices.GetRequiredService<EditionStore>().Current is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Дані завантажуються",
                        detail: "Сервіс щойно запустився і читає data.json. Спробуйте за кілька секунд.")
                        .ExecuteAsync(context);
                }
                return;
            }
            await next(context);
        });

    private static bool NeedsEdition(PathString path) =>
        !(path.StartsWithSegments("/health") || path.StartsWithSegments("/status") || path.StartsWithSegments("/error")
          || path.StartsWithSegments("/openapi") || path.StartsWithSegments("/site.css")
          || path.StartsWithSegments("/favicon.svg") || path.StartsWithSegments("/favicon.ico")
          || path.StartsWithSegments("/robots.txt") || path.StartsWithSegments("/og.png"));
}
