using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace DreamPovnota.Web.Live;

/// <summary>
/// Що користувач вписав у поле перевірки: код проєкту або посилання на його картку.
/// Із введеного береться тільки код; адресу запиту до DREAM складаємо самі,
/// тому жоден введений рядок не стає адресою (захист від SSRF).
/// </summary>
public static partial class CodeInput
{
    public const int MaxLength = 300;

    public const string Example =
        "Не схоже на код DREAM. Приклад: DREAM-UA-050923-B400F7B2 або посилання https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile";

    private static readonly string[] Hosts = ["dream.gov.ua", "www.dream.gov.ua"];
    private static readonly string[] Languages = ["ua", "en"];

    [GeneratedRegex(@"^(?:DREAM-UA-)?([0-9]{6})-([0-9A-F]{8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    /// <summary>Код без "DREAM-UA-" великими літерами або повідомлення для поля форми.</summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out string? code, [NotNullWhen(false)] out string? error)
    {
        code = null;
        string text = input?.Trim() ?? "";

        error = text switch
        {
            "" => "Вкажіть код проєкту DREAM або посилання на його картку",
            { Length: > MaxLength } => $"Не довше за {MaxLength} знаків",
            _ => null,
        };
        if (error is not null) return false;

        code = Code(text) ?? CodeFromAddress(text);
        if (code is not null) return true;

        error = text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "Посилання має починатися з https://dream.gov.ua"
            : Example;
        return false;
    }

    private static string? Code(string text)
    {
        var match = CodePattern().Match(text);
        return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value.ToUpperInvariant()}" : null;
    }

    // https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile і подібні
    private static string? CodeFromAddress(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || !uri.IsDefaultPort) return null;
        if (!Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)) return null;

        // AbsolutePath лишає %2F закодованим, тож прихований слеш не утворить зайвий сегмент
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return null;
        if (!Languages.Contains(segments[0], StringComparer.Ordinal) || segments[1] != "project") return null;
        return Code(segments[2]);
    }
}
