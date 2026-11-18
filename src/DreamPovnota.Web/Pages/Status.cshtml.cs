using DreamPovnota.Web.Import;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DreamPovnota.Web.Pages;

/// <summary>Сторінка для відповідей без тіла: 404, 429, 503 і решти кодів помилок.</summary>
[IgnoreAntiforgeryToken]
public sealed class StatusModel(EditionStore store) : PageModel
{
    public string Heading { get; private set; } = "";

    public string Text { get; private set; } = "";

    public bool Loading { get; private set; }

    public void OnGet(int status) => Describe(status);

    // сюди ж потрапляє POST /check, відхилений обмежувачем частоти
    public void OnPost(int status) => Describe(status);

    private void Describe(int status)
    {
        // під час повторного запуску код уже стоїть у відповіді; адресу /status/N можна відкрити й напряму
        bool reExecuted = HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is not null;
        int code = reExecuted ? Response.StatusCode : status;
        Loading = code == StatusCodes.Status503ServiceUnavailable && store.Current is null;
        (Heading, Text) = code switch
        {
            404 => ("Сторінки немає", "Такої сторінки, картки чи організації серед відкритих карток немає. Можливо, картку закрили або вилучили з DREAM."),
            429 => ("Забагато запитів", "З вашої адреси надійшло багато запитів поспіль. Зачекайте хвилину і спробуйте ще."),
            503 when Loading => ("Дані завантажуються", "Сервіс щойно запустився і читає свіжий випуск даних. Сторінка оновиться сама за кілька секунд."),
            503 => ("Сервіс тимчасово недоступний", "Спробуйте за кілька хвилин."),
            >= 500 => ("Щось пішло не так", "На сервері сталася помилка. Спробуйте пізніше."),
            _ => ("Запит не виконано", "Перевірте адресу і спробуйте ще."),
        };
        if (!reExecuted && code is >= 400 and <= 599) Response.StatusCode = code;
    }
}
