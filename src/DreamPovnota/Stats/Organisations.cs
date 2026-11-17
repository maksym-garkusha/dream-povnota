using DreamPovnota.Cards;

namespace DreamPovnota.Stats;

/// <summary>Як групувати картки за організацією, що їх веде.</summary>
public static class Organisations
{
    public const string SoleProprietorKey = "ipn";
    public const string NotSpecifiedKey = "none";
    public const string SoleProprietorTitle = "Фізична особа-підприємець";

    /// <summary>
    /// ЄДРПОУ, якщо він є. Іноземні організації й коди незвичної довжини групуємо
    /// за схемою і назвою. Підприємців розрізнити не можемо (код і ім'я не зберігаємо),
    /// тому вони всі в одній групі.
    /// </summary>
    public static string KeyOf(Party? party) => party switch
    {
        null => NotSpecifiedKey,
        { Edrpou: { } edrpou } => edrpou,
        { IsSoleProprietor: true } => SoleProprietorKey,
        _ => $"{party.Scheme}:{party.Name}",
    };

    public static string TitleOf(Party? party) => party switch
    {
        null => "Не вказано",
        { IsSoleProprietor: true } => SoleProprietorTitle,
        { Name: { } name } => name,
        _ => party.Edrpou ?? "Без назви",
    };
}
