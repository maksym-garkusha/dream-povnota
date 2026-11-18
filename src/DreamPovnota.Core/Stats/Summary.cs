using System.Globalization;
using DreamPovnota.Cards;
using DreamPovnota.Checks;
using DreamPovnota.Codes;
using DreamPovnota.Store;

namespace DreamPovnota.Stats;

/// <param name="Edrpou">null для іноземних організацій, підприємців і кодів незвичної довжини.</param>
public sealed record OrganisationStats(string? Edrpou, Slice Slice);

/// <summary>Коди сторін у відкритих картках. Для звіту, в наповненість не входить.</summary>
/// <param name="DistinctEdrpou">Різних восьмизначних кодів під схемою UA-EDR.</param>
/// <param name="InvalidEdrpou">Ті з них, що не проходять перевірку контрольного розряду.</param>
/// <param name="TenDigitParties">Сторін, у яких під схемою UA-EDR стоїть десятизначний номер (РНОКПП).</param>
/// <param name="TenDigitProjects">У скількох картках є така сторона.</param>
public sealed record CodeStats(int DistinctEdrpou, IReadOnlyList<string> InvalidEdrpou, int TenDigitParties, int TenDigitProjects);

/// <summary>Усе, що бачать сторінка і звіт: картки з результатами перевірок і зведення за розрізами.</summary>
/// <param name="AsOf">Дата зрізу, від неї рахується «оновлювалась за останній рік».</param>
/// <param name="Sync">Підсумок останньої синхронізації або null, якщо кеш старший за нього.</param>
/// <param name="Open">Відкриті картки, упорядковані за назвою.</param>
/// <param name="CodeDates">Найраніша і найпізніша дата в кодах проєктів (DREAM-UA-ддммрр-...).</param>
public sealed record Summary(
    DateTimeOffset AsOf,
    SyncInfo? Sync,
    int InCache,
    int Closed,
    IReadOnlyDictionary<string, int> UnknownStatuses,
    IReadOnlyList<CardResult> Open,
    Slice All,
    IReadOnlyList<Slice> Stages,
    IReadOnlyList<Slice> Regions,
    IReadOnlyList<Slice> Sectors,
    IReadOnlyList<OrganisationStats> Organisations,
    int SeekingFunding,
    int SeekingFundingWithEnglishTitle,
    int DescriptionStubs,
    int ProjectsWithMissingObjects,
    CodeStats Codes,
    (DateOnly First, DateOnly Last)? CodeDates)
{
    public static readonly StringComparer Ukrainian = StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);

    public int Unknown => UnknownStatuses.Values.Sum();

    public static Summary Build(
        IReadOnlyCollection<ProjectCard> projects,
        IEnumerable<ObjectCard> objects,
        DateTimeOffset asOf,
        SyncInfo? sync = null)
    {
        var objectsById = objects.ToDictionary(o => o.Id);
        var open = projects
            .Where(p => p.IsOpen)
            .Select(p => CheckList.Evaluate(ProjectFacts.Of(p, objectsById, asOf)))
            .OrderBy(r => SortKey(r.Card.Title), Ukrainian)
            .ThenBy(r => r.Card.Code, StringComparer.Ordinal)
            .ToList();

        return new Summary(
            asOf,
            sync,
            projects.Count,
            projects.Count(p => p.Stage == Stage.Closed),
            projects.Where(p => p.Stage == Stage.Unknown)
                .GroupBy(p => p.Status)
                .ToDictionary(g => g.Key, g => g.Count()),
            open,
            Slice.Of("all", "Усі відкриті", open),
            StagesOf(open),
            RegionsOf(open),
            SectorsOf(open),
            OrganisationsOf(open),
            open.Count(r => r.SeeksFunding),
            open.Count(r => r.SeeksFunding && r.Card.HasEnglishTitle),
            open.Count(r => r.Card.DescriptionState == TextState.Stub),
            open.Count(r => r.MissingObjects > 0),
            CodesOf(open.Select(r => r.Card)),
            CodeDatesOf(projects));
    }

    private static List<Slice> StagesOf(List<CardResult> open) =>
        Enum.GetValues<Stage>()
            .Where(s => s.IsOpen())
            .Select(s => Slice.Of(s.Key(), s.Title(), open.Where(r => r.Card.Stage == s).ToList()))
            .ToList();

    // області за назвою, «кілька» і «не вказано» в кінці: це не рейтинг
    private static List<Slice> RegionsOf(List<CardResult> open) =>
        open.GroupBy(r => r.RegionKey)
            .Select(g => Slice.Of(g.Key, Geo.Regions.Title(g.Key), g.ToList()))
            .OrderBy(s => s.Key is Geo.Regions.SeveralKey or Geo.Regions.NotSpecifiedKey)
            .ThenBy(s => s.Key == Geo.Regions.NotSpecifiedKey)
            .ThenBy(s => s.Title, Ukrainian)
            .ToList();

    public const string OtherSectorKey = "other";
    public const string OtherSectorTitle = "Інший код сектору";
    private const string NoSectorKey = "none";

    /// <summary>
    /// Літера сектору; "other", якщо код WB-ECO є, але його літери немає в довіднику
    /// (у даних трапляються O, OB, OC); "none", якщо коду немає. Так розріз за секторами
    /// не суперечить перевірці «Сектор»: картка з будь-яким кодом у «Не вказано» не потрапляє.
    /// </summary>
    public static string SectorKey(ProjectCard card) => card.SectorLetter switch
    {
        null => NoSectorKey,
        { } letter when Cards.Sectors.Letters.Contains(letter) => letter.ToString(),
        _ => OtherSectorKey,
    };

    private static string SectorTitle(string key) => key switch
    {
        NoSectorKey => Cards.Sectors.NotSpecified,
        OtherSectorKey => OtherSectorTitle,
        _ => Cards.Sectors.Name(key[0]),
    };

    // сектори за назвою, «інший код» і «не вказано» в кінці
    private static List<Slice> SectorsOf(List<CardResult> open) =>
        open.GroupBy(r => SectorKey(r.Card))
            .Select(g => Slice.Of(g.Key, SectorTitle(g.Key), g.ToList()))
            .OrderBy(s => s.Key is OtherSectorKey or NoSectorKey)
            .ThenBy(s => s.Key == NoSectorKey)
            .ThenBy(s => s.Title, Ukrainian)
            .ToList();

    private static List<OrganisationStats> OrganisationsOf(List<CardResult> open) =>
        open.GroupBy(r => Stats.Organisations.KeyOf(r.Card.Organisation))
            .Select(g =>
            {
                // назва з найсвіжішої картки: організації інколи перейменовуються; група не порожня
                var party = g.MaxBy(r => r.Card.Updated)!.Card.Organisation;
                return new OrganisationStats(party?.Edrpou, Slice.Of(g.Key, Stats.Organisations.TitleOf(party), g.ToList()));
            })
            .OrderBy(o => SortKey(o.Slice.Title), Ukrainian)
            .ThenBy(o => o.Slice.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Назва без лапок, дефісів і пробілів на початку: назви на зразок
    /// "----- Капітальний ремонт" чи «" ГІМНАЗІЯ "» інакше стоять першими в переліку.
    /// </summary>
    public static string SortKey(string title)
    {
        int start = 0;
        while (start < title.Length && !char.IsLetterOrDigit(title[start])) start++;
        return title[start..];
    }

    private static CodeStats CodesOf(IEnumerable<ProjectCard> cards)
    {
        var parties = cards.SelectMany(c => c.Parties.Select(p => (Card: c, Party: p))).ToList();
        var edrpou = parties.Select(x => x.Party.Edrpou).OfType<string>().Distinct().ToList();
        var tenDigit = parties.Where(x => x.Party.IsSoleProprietor && x.Party.Scheme == "UA-EDR").ToList();

        return new CodeStats(
            edrpou.Count,
            edrpou.Where(code => !Edrpou.IsValid(code)).Order(StringComparer.Ordinal).ToList(),
            tenDigit.Count,
            tenDigit.Select(x => x.Card.Id).Distinct().Count());
    }

    private static (DateOnly, DateOnly)? CodeDatesOf(IEnumerable<ProjectCard> cards)
    {
        var dates = cards.Select(c => CodeDate(c.Code)).OfType<DateOnly>().ToList();
        return dates.Count == 0 ? null : (dates.Min(), dates.Max());
    }

    /// <summary>
    /// Дата з коду "DREAM-UA-190424-A10FC799" (19.04.2024). У дев'яти картках з десяти
    /// вона збігається з <c>cdu_response.date</c>; у решті <c>date</c> пізніша, тому для
    /// дати створення надійніший код.
    /// </summary>
    public static DateOnly? CodeDate(string code)
    {
        string[] parts = code.Split('-');
        return parts.Length >= 3
            && DateOnly.TryParseExact(parts[2], "ddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }
}
