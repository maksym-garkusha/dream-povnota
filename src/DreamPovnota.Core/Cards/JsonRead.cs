using System.Globalization;
using System.Text.Json;

namespace DreamPovnota.Cards;

/// <summary>
/// Терпиме читання JSON: відсутнє поле чи значення не того типу дає null або
/// порожній перелік, а не виняток. На ранніх стадіях у картці немає цілих розділів.
/// </summary>
internal static class JsonRead
{
    public static JsonElement? Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    public static JsonElement? Prop(this JsonElement? element, string name) =>
        element is { } e ? e.Prop(name) : null;

    public static IEnumerable<JsonElement> Items(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    public static int Count(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.GetArrayLength() : 0;

    public static string? Text(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        // коди сторін інколи приходять числом
        { ValueKind: JsonValueKind.Number } e => e.GetRawText(),
        _ => null,
    };

    public static string? Text(this JsonElement element) => ((JsonElement?)element).Text();

    public static decimal? Number(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } e when e.TryGetDecimal(out var d) => d,
        { ValueKind: JsonValueKind.String } e when decimal.TryParse(
            e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    public static DateTimeOffset? Date(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } e && e.TryGetDateTimeOffset(out var date) ? date : null;
}
