using System.Globalization;
using DreamPovnota.Export;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Live;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DreamPovnota.Web.Pages;

/// <summary>
/// GET ніколи не ходить у DREAM: показує кешований результат або стан зі знімка.
/// У DREAM іде тільки POST з дійсним токеном форми, тож пошукові роботи
/// запитів назовні не викликають.
/// </summary>
// токен перевіряємо самі, щоб застарілу форму показати знову з поясненням, а не відповісти 400
[IgnoreAntiforgeryToken]
public sealed class CheckModel(EditionStore store, LiveCheck live, IAntiforgery antiforgery) : PageModel
{
    public const int RetryAfterSeconds = 60;

    public string? Code { get; private set; }

    public LiveOutcome? Outcome { get; private set; }

    public LoadedEdition Edition { get; private set; } = null!;

    public EditionCard? SnapshotCard { get; private set; }

    public string? Message { get; private set; }

    public string? Input { get; private set; }

    public string? InputError { get; private set; }

    public IActionResult OnGet(string? code)
    {
        Edition = store.Require();
        if (code is null) return Page();
        if (!DreamPovnota.Export.Edition.ShortCodePattern().IsMatch(code.ToUpperInvariant())) return NotFound();

        Code = code.ToUpperInvariant();
        SnapshotCard = Edition.CardIndexByCode.TryGetValue(Code, out int i) ? Edition.Edition.Cards[i] : null;
        Outcome = live.Cached(Code);
        Message = Outcome?.Status switch
        {
            LiveStatus.NotFound => $"Проєкту з кодом DREAM-UA-{Code} у DREAM немає.",
            _ => null,
        };
        if (Outcome?.Status == LiveStatus.NotFound) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync([FromForm(Name = "code")] string? input, CancellationToken ct)
    {
        Edition = store.Require();
        Input = input?.Length > CodeInput.MaxLength ? input[..CodeInput.MaxLength] : input;

        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return Form(StatusCodes.Status400BadRequest, "Сторінка застаріла, натисніть ще раз.");

        if (!CodeInput.TryParse(input, out string? code, out string? error))
            return Form(StatusCodes.Status400BadRequest, error);

        var outcome = await live.RunAsync(code, ct);
        switch (outcome.Status)
        {
            case LiveStatus.Found:
                Response.Headers.Location = $"/check/{code}";
                return StatusCode(StatusCodes.Status303SeeOther);
            case LiveStatus.NotFound:
                return Form(StatusCodes.Status404NotFound, $"Проєкту з кодом DREAM-UA-{code} у DREAM немає.");
            case LiveStatus.TooManyObjects:
                return Form(StatusCodes.Status422UnprocessableEntity,
                    $"У проєкті DREAM-UA-{code} понад {LiveCheck.MaxObjects} пов'язаних об'єктів, таку картку зараз не перевіряємо. Її стан є в знімку: /check/{code}.");
            case LiveStatus.Busy:
                int wait = Math.Max(1, (int)Math.Ceiling((outcome.RetryAfter ?? TimeSpan.FromSeconds(RetryAfterSeconds)).TotalSeconds));
                Response.Headers.RetryAfter = wait.ToString(CultureInfo.InvariantCulture);
                Message = $"Перевірок за останню годину забагато, сервіс обмежує запити до DREAM. Спробуйте за кілька хвилин. Стан картки в останньому знімку: /check/{code}.";
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Page();
            default:
                Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                Message = "DREAM зараз не відповідає, спробуйте за кілька хвилин.";
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Page();
        }
    }

    private PageResult Form(int status, string error)
    {
        InputError = error;
        Response.StatusCode = status;
        return Page();
    }
}
