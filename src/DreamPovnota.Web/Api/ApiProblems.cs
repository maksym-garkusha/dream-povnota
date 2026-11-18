using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace DreamPovnota.Web.Api;

/// <summary>
/// Відповіді API на помилки без власного тіла: невідома адреса, 405, збій у коді.
/// Пишемо Problem Details незалежно від Accept, бо клієнт API чекає JSON, навіть коли браузер просить HTML.
/// </summary>
public static class ApiProblems
{
    public static Task WriteStatusAsync(StatusCodeContext context)
    {
        int status = context.HttpContext.Response.StatusCode;
        string title = status == StatusCodes.Status404NotFound ? "Не знайдено" : ReasonPhrases.GetReasonPhrase(status);
        string? detail = status == StatusCodes.Status404NotFound ? "Такої адреси в API немає. Перелік адрес: /openapi/v1.json." : null;
        return Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context.HttpContext);
    }

    // текст винятку назовні не віддаємо; він є в журналі
    public static Task WriteErrorAsync(HttpContext context) =>
        Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Помилка сервісу",
            detail: "Запит не вдалося обробити. Спробуйте пізніше.").ExecuteAsync(context);
}
