using System.Numerics;
using DreamPovnota.Export;
using DreamPovnota.Stats;

namespace DreamPovnota.Web.Import;

/// <summary>Організація для сторінок: картки з однаковим ключем зведено в одну.</summary>
public sealed record OrganisationEntry(string Key, string? Edrpou, string Title, int Open, int Complete, double AverageScore);

/// <summary>
/// Випуск data.json разом з покажчиками, які сторінкам потрібні щоразу.
/// Будується один раз під час завантаження і далі тільки читається.
/// </summary>
public sealed class LoadedEdition
{
    /// <summary>Групи без ЄДРПОУ, які організаціями не є: підприємці і картки без сторони.</summary>
    public static readonly IReadOnlySet<string> NotOrganisations = new HashSet<string>(StringComparer.Ordinal)
    {
        Edition.OrganisationNamePrefix + Organisations.SoleProprietorTitle,
        Edition.OrganisationNamePrefix + Organisations.TitleOf(null),
    };

    public const string PortalPrefix = "https://dream.gov.ua/ua/project/DREAM-UA-";

    private readonly int scoreMask;

    public LoadedEdition(Edition edition, byte[] contentHash)
    {
        Edition = edition;
        ContentHash = contentHash;
        Sha256 = Convert.ToHexStringLower(contentHash);
        scoreMask = edition.Checks
            .Select((check, i) => check.CountsInScore ? 1 << i : 0)
            .Aggregate(0, (mask, bit) => mask | bit);

        CardIndexByCode = edition.Cards
            .Select((card, i) => (card.Code, i))
            .ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        CardsByOrganisation = Group(card => edition.Organisations[card.Organisation].Key);
        CardsByRegion = Group(card => edition.Regions[card.Region].Key);
        CheckIndex = Lookup(edition.Checks.Select(c => c.Code));
        RegionIndex = Lookup(edition.Regions.Select(r => r.Key));
        StageIndex = Lookup(edition.Stages.Select(s => s.Key));
        SectorIndex = Lookup(edition.Sectors.Select(s => s.Key));

        OrganisationsByKey = edition.Organisations
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .Where(g => !NotOrganisations.Contains(g.Key) && CardsByOrganisation.ContainsKey(g.Key))
            .Select(g => OrganisationOf(g.First(), CardsByOrganisation[g.Key]))
            .ToDictionary(o => o.Key, StringComparer.Ordinal);
        // порядок data.json: за назвою без лапок на початку
        OrganisationList = edition.Organisations
            .Select(o => o.Key)
            .Distinct(StringComparer.Ordinal)
            .Where(OrganisationsByKey.ContainsKey)
            .Select(key => OrganisationsByKey[key])
            .ToList();
    }

    public Edition Edition { get; }

    /// <summary>SHA-256 тіла файлу.</summary>
    public byte[] ContentHash { get; }

    /// <summary>Той самий SHA-256 у hex малими літерами, як його друкує sha256sum.</summary>
    public string Sha256 { get; }

    public IReadOnlyDictionary<string, int> CardIndexByCode { get; }

    public IReadOnlyDictionary<string, int[]> CardsByOrganisation { get; }

    public IReadOnlyDictionary<string, int[]> CardsByRegion { get; }

    public IReadOnlyDictionary<string, int> CheckIndex { get; }

    public IReadOnlyDictionary<string, int> RegionIndex { get; }

    public IReadOnlyDictionary<string, int> StageIndex { get; }

    public IReadOnlyDictionary<string, int> SectorIndex { get; }

    public IReadOnlyDictionary<string, OrganisationEntry> OrganisationsByKey { get; }

    /// <summary>Організації без груп підприємців і «не вказано», за назвою.</summary>
    public IReadOnlyList<OrganisationEntry> OrganisationList { get; }

    public int ScoreApplicable(EditionCard card) => BitOperations.PopCount((uint)(card.Applicable & scoreMask));

    public int ScoreFilled(EditionCard card) => BitOperations.PopCount((uint)(card.Filled & scoreMask));

    public double Score(EditionCard card) =>
        ScoreApplicable(card) == 0 ? 0 : (double)ScoreFilled(card) / ScoreApplicable(card);

    public bool IsComplete(EditionCard card) => ScoreApplicable(card) > 0 && ScoreFilled(card) == ScoreApplicable(card);

    /// <summary>Доречні, але не заповнені перевірки картки, разом з англійськими.</summary>
    public IEnumerable<EditionCheck> Missing(EditionCard card) =>
        Edition.Checks.Where((_, i) => (card.Applicable & (1 << i)) != 0 && (card.Filled & (1 << i)) == 0);

    public EditionOrganisation OrganisationOf(EditionCard card) => Edition.Organisations[card.Organisation];

    public IEnumerable<EditionCard> Cards(IEnumerable<int> indexes) => indexes.Select(i => Edition.Cards[i]);

    /// <summary>Організації, у яких є картки серед вказаних, за назвою.</summary>
    public IReadOnlyList<OrganisationEntry> OrganisationsAmong(IEnumerable<int> cardIndexes)
    {
        var keys = cardIndexes
            .Select(i => Edition.Organisations[Edition.Cards[i].Organisation].Key)
            .ToHashSet(StringComparer.Ordinal);
        return OrganisationList.Where(o => keys.Contains(o.Key)).ToList();
    }

    /// <summary>Скільки карток з <paramref name="cardIndexes"/> мають перевірку доречною і заповненою.</summary>
    public IReadOnlyList<CheckTally> Tally(IReadOnlyCollection<int> cardIndexes) =>
        Edition.Checks
            .Select((_, bit) => new CheckTally(
                cardIndexes.Count(i => (Edition.Cards[i].Applicable & (1 << bit)) != 0),
                cardIndexes.Count(i => (Edition.Cards[i].Filled & (1 << bit)) != 0)))
            .ToList();

    private OrganisationEntry OrganisationOf(EditionOrganisation first, int[] cardIndexes)
    {
        var cards = Cards(cardIndexes).ToList();
        return new OrganisationEntry(
            first.Key,
            first.Edrpou,
            first.Title,
            cards.Count,
            cards.Count(IsComplete),
            cards.Average(Score));
    }

    private Dictionary<string, int[]> Group(Func<EditionCard, string> key) =>
        Edition.Cards
            .Select((card, i) => (Key: key(card), i))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.i).ToArray(), StringComparer.Ordinal);

    private static Dictionary<string, int> Lookup(IEnumerable<string> keys) =>
        keys.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i, StringComparer.Ordinal);
}
