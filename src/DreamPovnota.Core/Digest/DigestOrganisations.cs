namespace DreamPovnota.Digest;

/// <summary>Організація, для якої збирається щотижневий зріз.</summary>
/// <param name="Title">Назва для сторінки й файлів; у картках DREAM її пишуть то великими літерами, то звичайними.</param>
public sealed record DigestOrganisation(string Edrpou, string Title);

/// <summary>
/// Державне агентство відновлення та розвитку інфраструктури України і обласні служби
/// відновлення. Перелік узято зі списку організацій data.json випуску 18.11.2026
/// (дані DREAM): назва містить «агентство відновлення» або «служба відновлення».
/// Він закріплений кодами, щоб зріз не залежав від того, як організацію записано
/// в картці. Нову службу в даних помітить <see cref="Unlisted"/>, а додати її сюди
/// треба руками.
/// </summary>
public static class DigestOrganisations
{
    public const string ListDate = "18.11.2026";

    /// <summary>Агентство першим, далі служби за назвою області.</summary>
    public static IReadOnlyList<DigestOrganisation> All { get; } =
    [
        new("37641918", "Державне агентство відновлення та розвитку інфраструктури України"),
        Service("25845655", "у Вінницькій"),
        Service("25843931", "у Дніпропетровській"),
        Service("03451526", "у Житомирській"),
        Service("25449824", "у Закарпатській"),
        Service("25891336", "у Запорізькій"),
        Service("25825434", "в Івано-Франківській"),
        Service("26345736", "у Київській"),
        Service("24718432", "у Кіровоградській"),
        Service("25253009", "у Львівській"),
        Service("25878206", "у Миколаївській"),
        Service("25829550", "в Одеській"),
        Service("25898491", "у Полтавській"),
        Service("25894919", "у Рівненській"),
        Service("24014538", "у Сумській"),
        Service("25887079", "у Тернопільській"),
        Service("30885376", "у Харківській"),
        Service("25899361", "у Херсонській"),
        Service("25879878", "у Хмельницькій"),
        Service("05422303", "у Черкаській"),
        Service("22843868", "у Чернівецькій"),
        Service("25901106", "у Чернігівській"),
    ];

    public static DigestOrganisation? Find(string edrpou) => All.FirstOrDefault(o => o.Edrpou == edrpou);

    /// <summary>Те саме правило, за яким складено перелік.</summary>
    public static bool LooksLikeRestorationBody(string title)
    {
        string lower = title.ToLowerInvariant();
        return lower.Contains("агентство відновлення", StringComparison.Ordinal)
            || lower.Contains("служба відновлення", StringComparison.Ordinal);
    }

    /// <summary>Організації з даних, схожі на агентство чи службу, яких у переліку немає.</summary>
    public static IReadOnlyList<(string? Edrpou, string Title)> Unlisted(IEnumerable<(string? Edrpou, string Title)> organisations) =>
        organisations
            .Where(o => LooksLikeRestorationBody(o.Title) && (o.Edrpou is null || Find(o.Edrpou) is null))
            .Distinct()
            .ToList();

    private static DigestOrganisation Service(string edrpou, string region) =>
        new(edrpou, $"Служба відновлення та розвитку інфраструктури {region} області");
}
