using DreamPovnota.Cards;

namespace DreamPovnota.Geo;

/// <summary>
/// Область за кодом КАТОТТГ: "UA" і 17 цифр, перші дві цифри це область.
/// Контрольного розряду в кодифікаторі немає, тому перевіряємо тільки форму
/// і чи існує такий код області.
/// </summary>
public static class Regions
{
    public const string Several = "Кілька областей";
    public const string NotSpecified = "Не вказано";

    /// <summary>Ключі для зведень і адреси сторінки: дві цифри області, "multi" або "none".</summary>
    public const string SeveralKey = "multi";
    public const string NotSpecifiedKey = "none";

    private static readonly Dictionary<string, string> Names = new()
    {
        ["01"] = "Автономна Республіка Крим",
        ["05"] = "Вінницька область",
        ["07"] = "Волинська область",
        ["12"] = "Дніпропетровська область",
        ["14"] = "Донецька область",
        ["18"] = "Житомирська область",
        ["21"] = "Закарпатська область",
        ["23"] = "Запорізька область",
        ["26"] = "Івано-Франківська область",
        ["32"] = "Київська область",
        ["35"] = "Кіровоградська область",
        ["44"] = "Луганська область",
        ["46"] = "Львівська область",
        ["48"] = "Миколаївська область",
        ["51"] = "Одеська область",
        ["53"] = "Полтавська область",
        ["56"] = "Рівненська область",
        ["59"] = "Сумська область",
        ["61"] = "Тернопільська область",
        ["63"] = "Харківська область",
        ["65"] = "Херсонська область",
        ["68"] = "Хмельницька область",
        ["71"] = "Черкаська область",
        ["73"] = "Чернівецька область",
        ["74"] = "Чернігівська область",
        ["80"] = "місто Київ",
        ["85"] = "місто Севастополь",
    };

    public static IReadOnlyCollection<string> AllNames => Names.Values;

    /// <summary>Дві цифри області або null, якщо код не схожий на КАТОТТГ чи такої області немає.</summary>
    public static string? RegionCode(string? katottg)
    {
        if (katottg is null || katottg.Length != 19 || !katottg.StartsWith("UA", StringComparison.Ordinal))
            return null;
        if (katottg.AsSpan(2).ContainsAnyExcept("0123456789"))
            return null;

        string region = katottg.Substring(2, 2);
        return Names.ContainsKey(region) ? region : null;
    }

    public static string? Name(string? regionCode) =>
        regionCode is not null && Names.TryGetValue(regionCode, out var name) ? name : null;

    /// <summary>Назва області, «Кілька областей» або «Не вказано». Неправильні коди пропускаємо.</summary>
    public static string Of(IEnumerable<string> katottg) => Title(KeyOf(katottg));

    /// <summary>Дві цифри області, <see cref="SeveralKey"/> або <see cref="NotSpecifiedKey"/>.</summary>
    public static string KeyOf(IEnumerable<string> katottg)
    {
        var regions = katottg.Select(RegionCode).OfType<string>().Distinct().Take(2).ToList();
        return regions.Count switch
        {
            0 => NotSpecifiedKey,
            1 => regions[0],
            _ => SeveralKey,
        };
    }

    /// <summary>Назва за ключем з <see cref="KeyOf"/>.</summary>
    public static string Title(string key) => key switch
    {
        SeveralKey => Several,
        _ => Name(key) ?? NotSpecified,
    };

    public static string Of(ProjectCard card, IReadOnlyDictionary<string, ObjectCard> objects) =>
        Of(ProjectKatottg(card, objects));

    /// <summary>
    /// Коди місця проєкту: з <c>approaches</c>, а якщо там порожньо, з пов'язаних об'єктів.
    /// На ранніх стадіях <c>approaches</c> ще немає, і місце є тільки в об'єкті.
    /// </summary>
    public static IReadOnlyList<string> ProjectKatottg(ProjectCard card, IReadOnlyDictionary<string, ObjectCard> objects)
    {
        if (card.Katottg.Count > 0) return card.Katottg;

        return card.ObjectIds
            .Select(id => objects.GetValueOrDefault(id))
            .OfType<ObjectCard>()
            .SelectMany(o => o.Katottg)
            .Distinct()
            .ToList();
    }
}
