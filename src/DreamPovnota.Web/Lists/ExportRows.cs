using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Lists;

/// <summary>
/// Рядки вивантаження: однакові колонки для CSV і xlsx. Числа тільки цілі,
/// щоб у таблиці не було сюрпризів з десятковим роздільником.
/// </summary>
public static class ExportRows
{
    private static readonly string[] CardColumns =
    [
        "Код DREAM", "Посилання", "Назва", "Організація", "ЄДРПОУ", "Область", "Стадія", "Сектор",
        "Оновлено", "Заповнено", "Доречно", "Наповненість, %", "Шукає фінансування", "Чого бракує",
    ];

    /// <summary>
    /// Колонки перевірок названі як перевірки. Назва перевірки «Сектор» збігається з колонкою
    /// сектору, а xlsx будується зі словника за назвою колонки, тож такі назви отримують уточнення.
    /// </summary>
    public static IReadOnlyList<string> Headers(LoadedEdition edition) =>
    [
        .. CardColumns,
        .. edition.Edition.Checks.Select(c => CardColumns.Contains(c.Title, StringComparer.Ordinal) ? c.Title + " (заповнено)" : c.Title),
    ];

    /// <summary>Значення рядка: string або int, null для порожньої клітинки.</summary>
    public static IEnumerable<object?[]> Rows(LoadedEdition edition, IEnumerable<int> cardIndexes)
    {
        var e = edition.Edition;
        foreach (int i in cardIndexes)
        {
            var card = e.Cards[i];
            var organisation = edition.OrganisationOf(card);
            object?[] row =
            [
                "DREAM-UA-" + card.Code,
                LoadedEdition.PortalPrefix + card.Code + "/profile",
                card.Title,
                organisation.Title,
                organisation.Edrpou,
                e.Regions[card.Region].Title,
                e.Stages[card.Stage].Title,
                e.Sectors[card.Sector].Title,
                card.Updated.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                edition.ScoreFilled(card),
                edition.ScoreApplicable(card),
                (int)Math.Round(edition.Score(card) * 100, MidpointRounding.AwayFromZero),
                card.SeeksFunding ? "так" : "ні",
                string.Join(", ", edition.Missing(card).Select(c => c.Title)),
                .. e.Checks.Select((_, bit) => (card.Applicable & (1 << bit)) == 0 ? null
                    : (card.Filled & (1 << bit)) != 0 ? "так" : "ні"),
            ];
            yield return row;
        }
    }

    public static string FileName(LoadedEdition edition, string extension) =>
        $"dream-povnota-{edition.Edition.Generated.UtcDateTime:yyyy-MM-dd}.{extension}";
}
