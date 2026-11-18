using System.Globalization;
using System.Numerics;
using DreamPovnota.Export;

namespace DreamPovnota.Digest;

/// <summary>
/// Зріз організації з випуску data.json і, якщо є, стану карток на початок тижня.
/// Числа рахуються так само, як на сторінці організації: відкриті картки,
/// «заповнено все доречне» без англійських полів.
/// </summary>
public static class DigestBuilder
{
    /// <summary>Адреса веб-сервісу, на яку ведуть посилання у файлах, хоч би де їх зібрали.</summary>
    public const string ServiceUrl = "https://dream-povnota.onrender.com";

    public const string PortalPrefix = "https://dream.gov.ua/ua/project/DREAM-UA-";

    public const int WeekDays = 7;

    public const int EasiestCount = 5;

    private static readonly StringComparer TitleOrder = StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: false);

    public static string PortalLink(string code) => PortalPrefix + code + "/profile";

    /// <summary>Сім днів до дати випуску включно, за UTC, як дати оновлення в data.json.</summary>
    public static DateOnly WeekTo(Edition edition) => DateOnly.FromDateTime(edition.Generated.UtcDateTime);

    public static DateOnly WeekFrom(Edition edition) => WeekTo(edition).AddDays(-(WeekDays - 1));

    /// <summary>Знімок історії для порівняння: останній не пізніше початку тижня.</summary>
    public static DateTimeOffset BaselineAt(Edition edition) =>
        new(WeekFrom(edition).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <param name="editionSha256">SHA-256 файлу data.json у hex: друкується у файлах зрізу.</param>
    public static OrganisationDigest Build(Edition edition, string editionSha256, DigestOrganisation organisation, DigestBaseline? baseline)
    {
        if (editionSha256.Length != 64 || !editionSha256.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("SHA-256 має бути 64 знаками hex малими літерами", nameof(editionSha256));

        int scoreMask = edition.Checks
            .Select((check, i) => check.CountsInScore ? 1 << i : 0)
            .Aggregate(0, (mask, bit) => mask | bit);
        var weekFrom = WeekFrom(edition);
        var weekTo = WeekTo(edition);

        var cards = edition.Cards
            .Where(c => edition.Organisations[c.Organisation].Edrpou == organisation.Edrpou)
            .OrderBy(c => c.Title, TitleOrder)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();

        var digestCards = cards.Select(c => Card(edition, c, scoreMask)).ToList();
        int complete = cards.Count(c => IsComplete(c, scoreMask));
        int updated = cards.Count(c => c.Updated >= weekFrom && c.Updated <= weekTo);

        var coverage = baseline is null ? HistoryCoverage.Unavailable
            : baseline.HistoryFrom > BaselineAt(edition) ? HistoryCoverage.Partial
            : HistoryCoverage.FullWeek;

        return new OrganisationDigest(
            organisation,
            edition.Generated,
            editionSha256,
            weekFrom,
            weekTo,
            digestCards,
            complete,
            updated,
            Easiest(edition, cards),
            Week(edition, organisation, cards, baseline, weekFrom, weekTo),
            coverage,
            baseline?.HistoryFrom,
            baseline?.Taken);
    }

    private static bool IsComplete(EditionCard card, int scoreMask)
    {
        int applicable = card.Applicable & scoreMask;
        return applicable != 0 && (card.Filled & scoreMask) == applicable;
    }

    private static DigestCard Card(Edition edition, EditionCard card, int scoreMask)
    {
        int applicable = BitOperations.PopCount((uint)(card.Applicable & scoreMask));
        int filled = BitOperations.PopCount((uint)(card.Filled & scoreMask));
        var missing = Missing(edition, card).ToList();

        // спершу те, що входить у наповненість; англійські поля тільки коли решту заповнено
        var first = missing.Where(c => c.CountsInScore).OrderBy(c => DigestEase.Rank(c.Code)).FirstOrDefault()
            ?? missing.OrderBy(c => DigestEase.Rank(c.Code)).FirstOrDefault();

        return new DigestCard(
            card.Code,
            card.Title,
            edition.Regions[card.Region].Title,
            edition.Stages[card.Stage].Title,
            edition.Sectors[card.Sector].Title,
            card.Updated,
            filled,
            applicable,
            applicable == 0 ? 0 : (int)Math.Round(100.0 * filled / applicable, MidpointRounding.AwayFromZero),
            missing.Select(c => c.Title).ToList(),
            first?.Title);
    }

    private static IEnumerable<EditionCheck> Missing(Edition edition, EditionCard card) =>
        edition.Checks.Where((_, i) => (card.Applicable & (1 << i)) != 0 && (card.Filled & (1 << i)) == 0);

    /// <summary>Не заповнені поля організації: найпростіші першими, серед однаково простих частіші.</summary>
    private static IReadOnlyList<EasyItem> Easiest(Edition edition, IReadOnlyList<EditionCard> cards) =>
        edition.Checks
            .Select((check, bit) => (check, count: cards.Count(c => (c.Applicable & (1 << bit)) != 0 && (c.Filled & (1 << bit)) == 0)))
            .Where(x => x.count > 0)
            .OrderBy(x => DigestEase.Rank(x.check.Code))
            .ThenByDescending(x => x.count)
            .Take(EasiestCount)
            .Select(x => new EasyItem(x.check.Code, x.check.Title, DigestEase.Hint(x.check.Code), x.count))
            .ToList();

    private static IReadOnlyList<WeekChange> Week(
        Edition edition, DigestOrganisation organisation, IReadOnlyList<EditionCard> cards,
        DigestBaseline? baseline, DateOnly weekFrom, DateOnly weekTo)
    {
        var changes = new List<WeekChange>();
        var then = baseline?.Cards.ToDictionary(c => c.Code, StringComparer.Ordinal) ?? [];
        // біти знімка і випуску можуть означати різні перевірки, тому порівнюємо за кодами
        var bitThen = baseline?.Checks.Select((code, i) => (code, i)).ToDictionary(x => x.code, x => x.i, StringComparer.Ordinal) ?? [];
        var stageTitles = edition.Stages.ToDictionary(s => s.Key, s => s.Title, StringComparer.Ordinal);

        foreach (var card in cards)
        {
            string stageNow = edition.Stages[card.Stage].Title;
            bool updatedThisWeek = card.Updated >= weekFrom && card.Updated <= weekTo;

            if (baseline is null || !then.TryGetValue(card.Code, out var old))
            {
                if (baseline is not null)
                    changes.Add(new WeekChange(card.Code, card.Title, WeekChangeKind.Appeared, card.Updated, null, stageNow, [], []));
                else if (updatedThisWeek)
                    changes.Add(new WeekChange(card.Code, card.Title, WeekChangeKind.Updated, card.Updated, null, stageNow, [], []));
                continue;
            }

            var nowFilled = new List<string>();
            var noLonger = new List<string>();
            for (int bit = 0; bit < edition.Checks.Count; bit++)
            {
                if (!bitThen.TryGetValue(edition.Checks[bit].Code, out int oldBit)) continue;
                bool filledNow = (card.Filled & (1 << bit)) != 0;
                bool filledThen = (old.Filled & (1 << oldBit)) != 0;
                if (filledNow && !filledThen) nowFilled.Add(edition.Checks[bit].Title);
                if (filledThen && !filledNow) noLonger.Add(edition.Checks[bit].Title);
            }

            string stageThen = stageTitles.GetValueOrDefault(old.Stage, old.Stage);
            bool changed = nowFilled.Count > 0 || noLonger.Count > 0 || stageThen != stageNow || old.Updated != card.Updated;
            if (changed || updatedThisWeek)
                changes.Add(new WeekChange(card.Code, card.Title, WeekChangeKind.Updated, card.Updated, stageThen, stageNow, nowFilled, noLonger));
        }

        if (baseline is not null)
        {
            var now = cards.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
            foreach (var old in baseline.Cards.Where(c => c.Edrpou == organisation.Edrpou && !now.Contains(c.Code)))
                changes.Add(new WeekChange(old.Code, old.Title, WeekChangeKind.Left, old.Updated,
                    stageTitles.GetValueOrDefault(old.Stage, old.Stage), null, [], []));
        }

        return changes
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Title, TitleOrder)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
    }
}
