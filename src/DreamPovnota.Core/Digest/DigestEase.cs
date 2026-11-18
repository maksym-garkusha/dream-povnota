namespace DreamPovnota.Digest;

/// <summary>
/// Наскільки просто дописати поле. Порядок наш, а не з DREAM: спершу те, що вибирається
/// зі списку або вміщається в рядок, далі тексти, потім файли й кошторис. Фінансування
/// й закупівля в кінці, бо залежать не від картки, а від того, що вже сталося з проєктом.
/// </summary>
public static class DigestEase
{
    private static readonly (string Code, string Hint)[] Order =
    [
        ("sector", "вибір зі списку"),
        ("title-en", "один рядок англійською"),
        ("duration", "одне число, місяців"),
        ("location", "громада з довідника КАТОТТГ"),
        ("purpose", "кілька речень про потребу громади"),
        ("description", "опис робіт"),
        ("description-en", "переклад опису"),
        ("documents", "файл або фото"),
        ("cost", "сума з кошторису"),
        ("fresh", "переглянути картку й оновити стан"),
        ("funding", "джерело грошей на закупівлю"),
        ("procurement", "посилання на закупівлю чи договір"),
    ];

    /// <summary>Місце поля в порядку; поля, яких тут немає, після всіх відомих.</summary>
    public static int Rank(string code)
    {
        int index = Array.FindIndex(Order, o => o.Code == code);
        return index < 0 ? Order.Length : index;
    }

    /// <summary>«Не заповнено у 3 картках з 16.»; для свіжості картки інше формулювання, бо це не поле.</summary>
    public static string Gap(string code, int cards, int open) => code == "fresh"
        ? $"Не оновлювались понад рік: {DigestText.Cards(cards)} з {DigestText.Number(open)}."
        : $"Не заповнено {DigestText.InCards(cards)} з {DigestText.Number(open)}.";

    public static string Hint(string code)
    {
        int index = Array.FindIndex(Order, o => o.Code == code);
        return index < 0 ? "" : Order[index].Hint;
    }
}
