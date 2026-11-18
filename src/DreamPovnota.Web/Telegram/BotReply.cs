using System.Text;
using DreamPovnota.Checks;
using DreamPovnota.Web.Live;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Telegram;

/// <summary>
/// Тексти відповідей бота. Звичайний текст без parse_mode: назва картки чи організації
/// з DREAM не може стати розміткою, тож і екранувати нічого. Посилання Telegram
/// підсвічує сам. Імен людей і кодів підприємців у <see cref="LiveResult"/> немає.
/// </summary>
public static class BotReply
{
    public const int MaxTitle = 120;

    /// <summary>Той самий підпис, що на сайті; стоїть і в кінці відповіді на картку, бо її пересилають далі.</summary>
    public const string Disclaimer = "Незалежний навчальний проєкт на відкритих даних, не офіційна статистика.";

    public static string Help(Uri site) =>
        "Перевіряю, наскільки заповнена картка проєкту в DREAM.\n\n" +
        "Надішліть код проєкту, наприклад " + CodeInput.ExampleCode + ", або посилання на картку з dream.gov.ua. " +
        "У відповідь буде стадія, які доречні для неї поля заповнено і які ні, і що дописати першим.\n\n" +
        "Картку забираю з відкритого API DREAM просто зараз, результат тримаю 10 хвилин. " +
        "Ні повідомлень, ні того, хто питав, бот не зберігає.\n\n" +
        Disclaimer + "\n" +
        "Сайт: " + site;

    public static string Invalid(string error) => error + "\nЩо вміє бот: /help";

    public static string Limited(TimeSpan retryAfter) =>
        $"Забагато повідомлень підряд. Спробуйте за {Minutes(retryAfter)}.";

    /// <param name="code">Код без "DREAM-UA-".</param>
    /// <param name="organisationOnSite">Чи є в сервісі сторінка організації з таким ЄДРПОУ.</param>
    public static string Check(string code, LiveOutcome outcome, Uri site, bool organisationOnSite) => outcome.Status switch
    {
        LiveStatus.Found when outcome.Result is { } result => Found(result, site, organisationOnSite),
        LiveStatus.NotFound => $"Проєкту з кодом DREAM-UA-{code} у DREAM немає.",
        LiveStatus.TooManyObjects =>
            $"У проєкті DREAM-UA-{code} понад {LiveCheck.MaxObjects} пов'язаних об'єктів, таку картку зараз не перевіряю. " +
            $"Її стан в останньому знімку: {SnapshotUrl(site, code)}",
        LiveStatus.Busy =>
            $"Перевірок за останню годину забагато, сервіс обмежує запити до DREAM. Спробуйте за {Minutes(outcome.RetryAfter ?? TimeSpan.FromMinutes(1))}. " +
            $"Стан картки в останньому знімку: {SnapshotUrl(site, code)}",
        _ => "DREAM зараз не відповідає, спробуйте за кілька хвилин.",
    };

    private static string Found(LiveResult result, Uri site, bool organisationOnSite)
    {
        var text = new StringBuilder();
        text.AppendLine(Shorten(result.Title, MaxTitle));
        text.AppendLine("DREAM-UA-" + result.Code);
        text.AppendLine("Стадія: " + result.StageTitle);
        text.AppendLine("Організація: " + Shorten(result.OrganisationTitle, MaxTitle));

        if (!result.IsOpen)
        {
            text.AppendLine();
            text.AppendLine("Картка закрита (скасована, помилкова, дублікат або не реалізована), тому поля не перевіряю.");
        }
        else
        {
            text.AppendLine($"Заповнено {result.ScoreFilled} з {result.ScoreApplicable} доречних полів.");
            if (result.MissingObjects > 0)
                text.AppendLine($"Пов'язаних об'єктів, яких DREAM не віддав: {result.MissingObjects}. Поля з них не враховано.");

            var missing = result.Missing.Where(c => c.CountsInScore).ToList();
            List(text, "Не заповнено:", missing);
            List(text, "Заповнено:", result.Filled.Where(c => c.CountsInScore));
            English(text, result);

            text.AppendLine();
            text.AppendLine(missing.Count > 0
                ? $"Дописати першим: {missing[0].Title}. {missing[0].Why}"
                : "Усе, що доречне на цій стадії, заповнено.");
        }

        text.AppendLine();
        text.AppendLine("Картка в DREAM: " + Format.PortalUrl(result.Code));
        if (organisationOnSite && result.Edrpou is { } edrpou)
            text.AppendLine("Усі картки організації: " + new Uri(site, Format.OrganisationPath(edrpou)));
        text.Append("Перевірено " + Format.DateTime(result.CheckedAt) + ".");

        // підпис додаємо після скорочення, щоб довга відповідь його не втратила
        return Shorten(text.ToString(), BotMessage.MaxLength - Disclaimer.Length - 1) + "\n" + Disclaimer;
    }

    private static void List(StringBuilder text, string title, IEnumerable<Check> checks)
    {
        var items = checks.ToList();
        if (items.Count == 0) return;
        text.AppendLine();
        text.AppendLine(title);
        foreach (var check in items) text.AppendLine("• " + check.Title);
    }

    // англійські поля в наповненість не входять, тому окремо і без «дописати першим»
    private static void English(StringBuilder text, LiveResult result)
    {
        var filled = result.Filled.Where(c => !c.CountsInScore).ToList();
        var missing = result.Missing.Where(c => !c.CountsInScore).ToList();
        if (filled.Count + missing.Count == 0) return;
        text.AppendLine();
        text.AppendLine("Англійською, поза підрахунком:");
        foreach (var check in filled) text.AppendLine("• є: " + check.Title);
        foreach (var check in missing) text.AppendLine("• немає: " + check.Title);
    }

    private static Uri SnapshotUrl(Uri site, string code) => new(site, "/check/" + code);

    private static string Minutes(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)) + " хв";

    private static string Shorten(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = max - 1;
        // не розрізати пару сурогатів: половина емодзі в JSON дає знак заміни або відмову Telegram
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }
}
