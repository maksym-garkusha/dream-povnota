using DreamPovnota.Web.Live;

namespace DreamPovnota.Web.Telegram;

public enum BotIntent
{
    Help,
    Check,
    Invalid,
}

/// <param name="Code">Для <see cref="BotIntent.Check"/>: код без "DREAM-UA-".</param>
/// <param name="Error">Для <see cref="BotIntent.Invalid"/>: що не так і як має виглядати.</param>
public sealed record BotRequest(BotIntent Intent, string? Code = null, string? Error = null)
{
    public static readonly BotRequest Help = new(BotIntent.Help);
}

/// <summary>
/// Що хоче користувач бота: довідку або перевірку картки. Код дістаємо тим самим
/// <see cref="CodeInput"/>, що й форма /check, тож адресу запиту до DREAM і тут
/// складаємо самі, а не беремо з повідомлення.
/// </summary>
public static class BotMessage
{
    /// <summary>Довше повідомлення Telegram не пропускає.</summary>
    public const int MaxLength = 4096;

    // код чи посилання зазвичай в одному з перших слів; далі не шукаємо, щоб довгий текст не коштував дорого
    private const int MaxWords = 40;

    private static readonly char[] Spaces = [' ', '\n', '\r', '\t'];
    private static readonly char[] Punctuation = ['.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '<', '>', '«', '»', '"', '\''];

    public const string CheckWithoutCode = "Після /check додайте код, наприклад /check DREAM-UA-050923-B400F7B2";

    public const string UnknownCommand = "Такої команди немає. Надішліть код проєкту DREAM або посилання на картку, довідка: /help";

    public static BotRequest Parse(string? text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxLength) return Invalid(CodeInput.Example);
        if (trimmed[0] == '/') return Command(trimmed);
        return Find(trimmed);
    }

    // "/check@назва_бота DREAM-UA-..." у групі, "/start 050923-B400F7B2" з посилання t.me/бот?start=...
    private static BotRequest Command(string text)
    {
        int end = text.IndexOfAny(Spaces);
        string word = end < 0 ? text : text[..end];
        string rest = end < 0 ? "" : text[end..].Trim();
        int at = word.IndexOf('@', StringComparison.Ordinal);
        string command = (at < 0 ? word : word[..at]).ToLowerInvariant();

        return command switch
        {
            "/start" when rest.Length > 0 && CodeInput.TryParse(rest, out string? code, out _) => Check(code),
            "/start" or "/help" => BotRequest.Help,
            "/check" when rest.Length == 0 => Invalid(CheckWithoutCode),
            "/check" => Find(rest),
            _ => Invalid(UnknownCommand),
        };
    }

    /// <summary>Увесь текст як код чи посилання, а якщо ні, перше слово, що ним є.</summary>
    private static BotRequest Find(string text)
    {
        if (CodeInput.TryParse(text, out string? code, out string? error)) return Check(code);

        foreach (string word in text.Split(Spaces, MaxWords + 1, StringSplitOptions.RemoveEmptyEntries).Take(MaxWords))
        {
            if (CodeInput.TryParse(word.Trim(Punctuation), out code, out _)) return Check(code);
        }
        return Invalid(text.Length > CodeInput.MaxLength ? CodeInput.Example : error);
    }

    private static BotRequest Check(string code) => new(BotIntent.Check, Code: code);

    private static BotRequest Invalid(string error) => new(BotIntent.Invalid, Error: error);
}
