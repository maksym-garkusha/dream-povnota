using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Lists;

/// <summary>
/// Рядки вивантаження: однакові колонки для CSV і xlsx. Числа тільки цілі,
/// щоб у таблиці не було сюрпризів з десятковим роздільником.
/// </summary>
public static class ExportRows
{
    public static IReadOnlyList<string> Headers(LoadedEdition edition) =>
    [
        "Код DREAM", "Посилання", "Назва", "Організація", "ЄДРПОУ", "Область", "Стадія", "Сектор",
        "Оновлено", "Заповнено", "Доречно", "Наповненість, %", "Шукає фінансування", "Чого бракує",
        .. edition.Edition.Checks.Select(c => c.Title),
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
