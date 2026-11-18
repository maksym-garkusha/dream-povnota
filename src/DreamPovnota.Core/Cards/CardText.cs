using System.Net;
using System.Text.RegularExpressions;

namespace DreamPovnota.Cards;

public enum TextState
{
    /// <summary>Поля немає або після чищення лишився порожній рядок.</summary>
    Empty,

    /// <summary>Заглушка на зразок NULL або "-".</summary>
    Stub,

    /// <summary>Справжній текст, але коротший за поріг.</summary>
    TooShort,

    Filled,
}

/// <summary>Чищення текстових полів картки, які DREAM віддає в HTML.</summary>
public static partial class CardText
{
    // те, що ініціатори пишуть замість тексту, коли поле обов'язкове в формі
    private static readonly string[] Stubs = ["NULL", "-", ".", "N/A", "НЕМАЄ", "НЕМА"];

    /// <summary>Знімає теги, розкодовує сутності (&amp;nbsp; тощо) і згортає пробіли.</summary>
    public static string Clean(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // тег замінюємо пробілом, щоб "<p>один</p><p>два</p>" не злиплись в одне слово
        string text = Tag().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        return Spaces().Replace(text, " ").Trim();
    }

    /// <summary>Згортає пробіли й переноси рядків у звичайному тексті (назви).</summary>
    public static string OneLine(string? text) =>
        string.IsNullOrEmpty(text) ? "" : Spaces().Replace(text, " ").Trim();

    public static bool IsStub(string cleaned) =>
        cleaned.Length == 0 || Stubs.Any(s => string.Equals(cleaned, s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Стан поля. Поріг довжини рахується по очищеному тексту.</summary>
    public static TextState Classify(string? html, int minLength = 0)
    {
        string text = Clean(html);
        if (text.Length == 0) return TextState.Empty;
        if (IsStub(text)) return TextState.Stub;
        if (text.Length < minLength) return TextState.TooShort;
        return TextState.Filled;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
