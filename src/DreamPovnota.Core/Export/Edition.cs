using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DreamPovnota.Stats;

namespace DreamPovnota.Export;

/// <summary>Перевірка так, як її описано в data.json.</summary>
public sealed record EditionCheck(
    string Code,
    string Title,
    string Group,
    bool CountsInScore,
    string From,
    string Until,
    string? Condition,
    string Why);

/// <summary>Розріз з data.json: усі картки, стадія, область чи сектор.</summary>
/// <param name="Checks">По одному на перевірку, у порядку <see cref="Edition.Checks"/>.</param>
public sealed record EditionSlice(
    string Key,
    string Title,
    int Open,
    int Complete,
    double AverageScore,
    IReadOnlyList<CheckTally> Checks);

/// <summary>
/// Організація з data.json. Ключ такий самий, як на сторінці: ЄДРПОУ, а без нього
/// "n:" і назва. Дві організації без коду з однаковою назвою мають один ключ.
/// </summary>
public sealed record EditionOrganisation(string Key, string? Edrpou, string Title, int Open, double AverageScore);

/// <summary>Відкрита картка з data.json. Індекси вказують у списки <see cref="Edition"/>.</summary>
/// <param name="Code">Код без "DREAM-UA-".</param>
public sealed record EditionCard(
    string Code,
    string Title,
    int Organisation,
    int Region,
    int Stage,
    int Sector,
    DateOnly Updated,
    int Applicable,
    int Filled,
    bool SeeksFunding);

/// <summary>
/// data.json, прочитаний назад: його публікує CLI, а веб-сервіс і тести читають.
/// Файл перевіряється повністю; якщо щось не сходиться, краще лишити попередній
/// випуск, ніж показати неправильні числа.
/// </summary>
public sealed partial record Edition(
    DateTimeOffset Generated,
    DateTimeOffset? Synced,
    IReadOnlyList<EditionCheck> Checks,
    EditionSlice All,
    IReadOnlyList<EditionSlice> Stages,
    IReadOnlyList<EditionSlice> Regions,
    IReadOnlyList<EditionSlice> Sectors,
    IReadOnlyList<EditionOrganisation> Organisations,
    IReadOnlyList<EditionCard> Cards)
{
    public const string OrganisationNamePrefix = "n:";

    /// <summary>Маски в int, тому перевірок не більше 31.</summary>
    public const int MaxChecks = 31;

    /// <summary>Назви карток у файлі обрізано до 160 знаків, тож 200 це вже зіпсований файл.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Назви організацій не обрізаються; у даних найдовша близько 200 знаків.</summary>
    public const int MaxOrganisationTitleLength = 400;

    [GeneratedRegex(@"^\d{6}-[0-9A-F]{8}$")]
    public static partial Regex ShortCodePattern();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex EdrpouPattern();

    public static string OrganisationKey(string? edrpou, string title) => edrpou ?? OrganisationNamePrefix + title;

    /// <exception cref="InvalidDataException">Файл іншої версії або з даними, що не сходяться.</exception>
    public static async Task<Edition> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"data.json не прочитано: {ex.Message}", ex);
        }
    }

    private static Edition Read(JsonElement root)
    {
        int version = root.GetProperty("v").GetInt32();
        if (version != DataExport.Version)
            throw new InvalidDataException($"data.json версії {version}, очікували {DataExport.Version}");

        var checks = root.GetProperty("checks").EnumerateArray().Select(ReadCheck).ToList();
        if (checks.Count is 0 or > MaxChecks)
            throw new InvalidDataException($"Перевірок {checks.Count}, має бути від 1 до {MaxChecks}");

        var stages = ReadSlices(root.GetProperty("stages"), checks.Count);
        var regions = ReadSlices(root.GetProperty("regions"), checks.Count);
        var sectors = ReadSlices(root.GetProperty("sectors"), checks.Count);
        var organisations = root.GetProperty("orgs").EnumerateArray().Select(ReadOrganisation).ToList();

        var cards = root.GetProperty("p").EnumerateArray()
            .Select(p => ReadCard(p, organisations.Count, regions.Count, stages.Count, sectors.Count))
            .ToList();

        int open = root.GetProperty("counts").GetProperty("open").GetInt32();
        if (open != cards.Count)
            throw new InvalidDataException($"У counts.open {open}, а карток {cards.Count}");

        var synced = root.GetProperty("synced");
        return new Edition(
            root.GetProperty("generated").GetDateTimeOffset(),
            synced.ValueKind == JsonValueKind.Null ? null : synced.GetDateTimeOffset(),
            checks,
            ReadSlice(root.GetProperty("all"), checks.Count),
            stages,
            regions,
            sectors,
            organisations,
            cards);
    }

    private static EditionCheck ReadCheck(JsonElement c) => new(
        c.GetProperty("c").GetString()!,
        Title(c.GetProperty("t")),
        c.GetProperty("g").GetString()!,
        c.GetProperty("score").GetBoolean(),
        c.GetProperty("from").GetString()!,
        c.GetProperty("until").GetString()!,
        c.TryGetProperty("if", out var condition) ? condition.GetString() : null,
        c.GetProperty("why").GetString()!);

    private static List<EditionSlice> ReadSlices(JsonElement array, int checkCount) =>
        array.EnumerateArray().Select(s => ReadSlice(s, checkCount)).ToList();

    private static EditionSlice ReadSlice(JsonElement s, int checkCount)
    {
        var tallies = s.GetProperty("c").EnumerateArray()
            .Select(t => new CheckTally(t[0].GetInt32(), t[1].GetInt32()))
            .ToList();
        if (tallies.Count != checkCount)
            throw new InvalidDataException($"У розрізі {s.GetProperty("k").GetString()} {tallies.Count} перевірок замість {checkCount}");

        return new EditionSlice(
            s.GetProperty("k").GetString()!,
            Title(s.GetProperty("t")),
            s.GetProperty("n").GetInt32(),
            s.GetProperty("full").GetInt32(),
            s.GetProperty("avg").GetDouble(),
            tallies);
    }

    // [ЄДРПОУ або null, назва, відкритих проєктів, середня наповненість]
    private static EditionOrganisation ReadOrganisation(JsonElement o)
    {
        string? edrpou = o[0].ValueKind == JsonValueKind.Null ? null : o[0].GetString();
        if (edrpou is not null && !EdrpouPattern().IsMatch(edrpou))
            throw new InvalidDataException($"ЄДРПОУ організації не з 8 цифр: {edrpou}");

        string title = Title(o[1], MaxOrganisationTitleLength);
        return new EditionOrganisation(OrganisationKey(edrpou, title), edrpou, title, o[2].GetInt32(), o[3].GetDouble());
    }

    // [код, назва, організація, область, стадія, сектор, дата зміни, доречні, заповнені, шукає фінансування 0/1]
    private static EditionCard ReadCard(JsonElement p, int organisations, int regions, int stages, int sectors)
    {
        string code = p[0].GetString()!;
        if (!ShortCodePattern().IsMatch(code))
            throw new InvalidDataException($"Код картки не схожий на код DREAM: {code}");

        int applicable = p[7].GetInt32();
        int filled = p[8].GetInt32();
        if ((filled & ~applicable) != 0)
            throw new InvalidDataException($"У картці {code} заповнено те, що не доречне");

        return new EditionCard(
            code,
            Title(p[1]),
            Index(p[2], organisations, code),
            Index(p[3], regions, code),
            Index(p[4], stages, code),
            Index(p[5], sectors, code),
            DateOnly.ParseExact(p[6].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            applicable,
            filled,
            p[9].GetInt32() == 1);
    }

    private static int Index(JsonElement value, int count, string code)
    {
        int index = value.GetInt32();
        if (index < 0 || index >= count)
            throw new InvalidDataException($"У картці {code} індекс {index} поза списком з {count}");
        return index;
    }

    private static string Title(JsonElement value, int maxLength = MaxTitleLength)
    {
        string title = value.GetString() ?? throw new InvalidDataException("Назва порожня");
        if (title.Length == 0 || title.Length > maxLength)
            throw new InvalidDataException($"Назва порожня або довша за {maxLength} знаків");
        return title;
    }
}
