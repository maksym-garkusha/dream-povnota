namespace DreamPovnota.Cards;

/// <summary>
/// Сектори з класифікації Світового банку (схема WB-ECO). Код двобуквений,
/// перша літера означає сектор, друга підсектор: "ES" це освіта, середня школа.
/// </summary>
public static class Sectors
{
    public const string NotSpecified = "Не вказано";

    private static readonly Dictionary<char, string> Names = new()
    {
        ['A'] = "Сільське господарство",
        ['B'] = "Державне управління",
        ['C'] = "Зв'язок та ІТ",
        ['E'] = "Освіта",
        ['F'] = "Фінанси",
        ['H'] = "Охорона здоров'я",
        ['L'] = "Енергетика та видобування",
        ['S'] = "Соціальний захист",
        ['T'] = "Транспорт",
        ['W'] = "Вода, каналізація та відходи",
        ['Y'] = "Промисловість, торгівля, послуги, житло",
    };

    public static IReadOnlyCollection<char> Letters => Names.Keys;

    public static string Name(char? letter) =>
        letter is { } l && Names.TryGetValue(l, out var name) ? name : NotSpecified;
}
