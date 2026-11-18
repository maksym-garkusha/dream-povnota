using System.Globalization;
using DreamPovnota.Export;
using DreamPovnota.Web.Import;
using Microsoft.Extensions.Primitives;

namespace DreamPovnota.Web.Lists;

/// <summary>
/// Фільтр переліку карток з адреси запиту. Один для сторінки, вивантажень і API,
/// тому й повідомлення про помилки однакові всюди. Дозволені значення беруться
/// з поточного випуску: невідома область чи стадія це помилка, а не порожній перелік.
/// </summary>
public sealed record CardFilter(
    string? Query,
    string? Region,
    string? Organisation,
    string? Stage,
    string? Sector,
    string? Missing,
    bool SeeksFunding,
    int Page,
    int PageSize)
{
    public const int MaxQueryLength = 100;
    public const int MaxKeyLength = 400;
    public const int MaxPage = 1000;
    public const int PageSizeOnPages = 50;
    public const int DefaultApiPageSize = 100;
    public const int MaxApiPageSize = 500;

    public static readonly CardFilter Empty = new(null, null, null, null, null, null, false, 1, PageSizeOnPages);

    /// <summary>Параметри, від яких залежить вибірка. За ними ж і тільки за ними кешуються вивантаження.</summary>
    public static readonly string[] FilterNames = ["q", "region", "org", "stage", "sector", "missing", "seeks"];

    private static readonly string[] Names = [.. FilterNames, "page", "pageSize"];

    /// <param name="allowPageSize">Розмір сторінки можна задати тільки в API.</param>
    /// <returns>Помилки за назвою параметра; порожньо, якщо фільтр правильний.</returns>
    public static Dictionary<string, string[]> TryParse(
        IQueryCollection query, LoadedEdition edition, bool allowPageSize, out CardFilter filter)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Error(string name, string message) => errors[name] = [message];

        string? Single(string name)
        {
            StringValues values = query[name];
            if (values.Count > 1) Error(name, "Параметр вказано більше одного разу");
            string? value = values.Count == 1 ? values[0]?.Trim() : null;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        string? q = Single("q");
        if (q is { Length: > MaxQueryLength }) Error("q", $"Не довше за {MaxQueryLength} знаків");

        string? region = Single("region");
        if (region is not null && !edition.RegionIndex.ContainsKey(region)) Error("region", "Такої області в даних немає");

        string? org = Single("org");
        if (org is { Length: > MaxKeyLength } || (org is not null && !edition.CardsByOrganisation.ContainsKey(org)))
            Error("org", "Такої організації серед відкритих карток немає");
        else if (org is not null && LoadedEdition.NotOrganisations.Contains(org))
            Error("org", "За цією групою перелік не показуємо");

        string? stage = Single("stage");
        if (stage is not null && !edition.StageIndex.ContainsKey(stage)) Error("stage", "Невідома стадія");

        string? sector = Single("sector");
        if (sector is not null && !edition.SectorIndex.ContainsKey(sector)) Error("sector", "Невідомий сектор");

        string? missing = Single("missing");
        if (missing is not null && !edition.CheckIndex.ContainsKey(missing)) Error("missing", "Невідоме поле");

        bool seeks = false;
        switch (Single("seeks"))
        {
            case null:
                break;
            case "1" or "true" or "on":
                seeks = true;
                break;
            default:
                Error("seeks", "Значення 1 або порожньо");
                break;
        }

        int page = Number(Single("page"), 1, MaxPage, 1, "page", $"Номер сторінки від 1 до {MaxPage}", Error);

        int pageSize = PageSizeOnPages;
        string? pageSizeText = Single("pageSize");
        if (allowPageSize)
            pageSize = Number(pageSizeText, 1, MaxApiPageSize, DefaultApiPageSize, "pageSize", $"Розмір сторінки від 1 до {MaxApiPageSize}", Error);
        else if (pageSizeText is not null)
            Error("pageSize", "Розмір сторінки тут не задається");

        foreach (string name in query.Keys.Where(k => !Names.Contains(k, StringComparer.Ordinal)))
            Error(name, "Невідомий параметр");

        filter = new CardFilter(q, region, org, stage, sector, missing, seeks, page, pageSize);
        return errors;
    }

    /// <summary>
    /// Фільтр для вивантаження. Файл завжди містить усю вибірку, тож page, pageSize і незнайомі
    /// параметри на нього не впливають: їх пропускаємо, щоб вони не множили записи в кеші.
    /// </summary>
    public static Dictionary<string, string[]> TryParseForExport(IQueryCollection query, LoadedEdition edition, out CardFilter filter)
    {
        var known = query
            .Where(p => FilterNames.Contains(p.Key, StringComparer.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return TryParse(new QueryCollection(known), edition, allowPageSize: false, out filter);
    }

    private static int Number(string? text, int min, int max, int fallback, string name, string message, Action<string, string> error)
    {
        if (text is null) return fallback;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max) return n;
        error(name, message);
        return fallback;
    }

    /// <summary>Індекси карток, що проходять фільтр, у порядку data.json (за назвою).</summary>
    public IReadOnlyList<int> Apply(LoadedEdition edition)
    {
        IEnumerable<int> indexes = Organisation is not null ? edition.CardsByOrganisation[Organisation]
            : Region is not null ? edition.CardsByRegion[Region]
            : Enumerable.Range(0, edition.Edition.Cards.Count);

        int region = Region is null ? -1 : edition.RegionIndex[Region];
        int stage = Stage is null ? -1 : edition.StageIndex[Stage];
        int sector = Sector is null ? -1 : edition.SectorIndex[Sector];
        int missingBit = Missing is null ? 0 : 1 << edition.CheckIndex[Missing];
        string? code = Query is null ? null : ShortCode(Query);

        return indexes.Where(i =>
        {
            var card = edition.Edition.Cards[i];
            return (region < 0 || card.Region == region)
                && (stage < 0 || card.Stage == stage)
                && (sector < 0 || card.Sector == sector)
                && (missingBit == 0 || ((card.Applicable & missingBit) != 0 && (card.Filled & missingBit) == 0))
                && (!SeeksFunding || card.SeeksFunding)
                && (Query is null
                    || card.Title.Contains(Query, StringComparison.OrdinalIgnoreCase)
                    || card.Code.Contains(code!, StringComparison.Ordinal));
        }).ToList();
    }

    private static string ShortCode(string query)
    {
        const string prefix = "DREAM-UA-";
        string upper = query.ToUpperInvariant();
        return upper.StartsWith(prefix, StringComparison.Ordinal) ? upper[prefix.Length..] : upper;
    }

    /// <summary>Параметри фільтра для посилань: без порожніх, з іншою сторінкою за потреби.</summary>
    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Query is not null) values["q"] = Query;
        if (Region is not null) values["region"] = Region;
        if (Organisation is not null) values["org"] = Organisation;
        if (Stage is not null) values["stage"] = Stage;
        if (Sector is not null) values["sector"] = Sector;
        if (Missing is not null) values["missing"] = Missing;
        if (SeeksFunding) values["seeks"] = "1";
        if ((page ?? Page) > 1) values["page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture);
        return values;
    }

    public string QueryString(int? page = null) =>
        Microsoft.AspNetCore.Http.QueryString.Create(RouteValues(page).Select(kv => KeyValuePair.Create(kv.Key, (string?)kv.Value))).Value ?? "";

    public bool IsEmpty => RouteValues(1).Count == 0;
}
