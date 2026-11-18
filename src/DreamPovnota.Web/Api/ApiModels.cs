namespace DreamPovnota.Web.Api;

public sealed record OrganisationRef(string Key, string? Edrpou, string Title);

public sealed record RegionRef(string Key, string Title);

/// <param name="Filled">Заповнених перевірок, що входять у наповненість (без англійських).</param>
/// <param name="Applicable">Доречних перевірок, що входять у наповненість.</param>
/// <param name="Missing">Коди доречних, але не заповнених перевірок, разом з англійськими.</param>
public sealed record CardItem(
    string Code,
    string Url,
    string Title,
    OrganisationRef Org,
    RegionRef Region,
    string Stage,
    string Sector,
    DateOnly Updated,
    int Filled,
    int Applicable,
    IReadOnlyList<string> Missing,
    bool SeeksFunding);

public sealed record CardsPage(int Total, int Page, int PageSize, DateTimeOffset EditionGenerated, IReadOnlyList<CardItem> Items);

public sealed record OrganisationItem(string Key, string? Edrpou, string Title, int Open, double AvgScore);

public sealed record OrganisationsPage(int Total, int Page, int PageSize, IReadOnlyList<OrganisationItem> Items);

public sealed record CheckCount(string Code, int Applicable, int Filled);

public sealed record SliceItem(string Key, string Title, int Open, int Complete, double AvgScore, IReadOnlyList<CheckCount> Checks);

public sealed record EditionInfo(DateTimeOffset Generated, DateTimeOffset? Synced, int Open);

public sealed record CheckInfo(string Code, string Title, string Group, bool Score, string From, string Until, string? Condition, string Why);

public sealed record KeyTitle(string Key, string Title);

public sealed record MetaInfo(
    EditionInfo Edition,
    IReadOnlyList<CheckInfo> Checks,
    IReadOnlyList<KeyTitle> Regions,
    IReadOnlyList<KeyTitle> Stages,
    IReadOnlyList<KeyTitle> Sectors);
