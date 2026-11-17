using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DreamPovnota.Cards;
using DreamPovnota.Checks;
using DreamPovnota.Stats;

namespace DreamPovnota.Export;

/// <summary>
/// data.json для сторінки. Файл один і компактний: проєкти лежать масивами,
/// а організації, області, стадії й сектори в словниках, на які проєкти
/// посилаються індексами. Що в якому полі, описано в README.
/// </summary>
public static class DataExport
{
    public const string FileName = "data.json";
    public const int MaxTitleLength = 160;

    /// <summary>Міняється, коли змінюється форма файлу; сторінка його перевіряє.</summary>
    public const int Version = 1;

    private const string CodePrefix = "DREAM-UA-";

    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static async Task WriteAsync(Summary summary, string siteDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(siteDirectory);
        string path = Path.Combine(siteDirectory, FileName);
        string temp = path + ".tmp";

        await using (var file = File.Create(temp))
        {
            Write(summary, file);
            await file.FlushAsync(ct);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static void Write(Summary summary, Stream stream)
    {
        using var json = new Utf8JsonWriter(stream, Options);
        json.WriteStartObject();
        json.WriteNumber("v", Version);
        json.WriteString("generated", summary.AsOf.UtcDateTime);
        if (summary.Sync is { } sync) json.WriteString("synced", sync.Finished.UtcDateTime);
        else json.WriteNull("synced");

        WriteCounts(json, summary);
        WriteChecks(json);

        json.WritePropertyName("all");
        WriteSlice(json, summary.All);
        WriteSlices(json, "stages", summary.Stages);
        WriteSlices(json, "regions", summary.Regions);
        WriteSlices(json, "sectors", summary.Sectors);
        WriteOrganisations(json, summary.Organisations);
        WriteProjects(json, summary);

        json.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter json, Summary summary)
    {
        json.WriteStartObject("counts");
        // без sync.json не знаємо, скільки було в стрічці і скільки не розібрано
        if (summary.Sync is { } sync)
        {
            json.WriteNumber("feed", sync.ProjectsInFeed);
            json.WriteNumber("unparsed", sync.UnparsedIds.Count);
        }
        else
        {
            json.WriteNull("feed");
            json.WriteNull("unparsed");
        }
        json.WriteNumber("cache", summary.InCache);
        json.WriteNumber("open", summary.Open.Count);
        json.WriteNumber("closed", summary.Closed);
        json.WriteNumber("unknown", summary.Unknown);
        json.WriteNumber("orgs", summary.Organisations.Count);
        json.WriteNumber("seeking", summary.SeekingFunding);
        json.WriteNumber("seekingEn", summary.SeekingFundingWithEnglishTitle);
        json.WriteEndObject();
    }

    private static void WriteChecks(Utf8JsonWriter json)
    {
        json.WriteStartArray("checks");
        foreach (var check in CheckList.All)
        {
            json.WriteStartObject();
            json.WriteString("c", check.Code);
            json.WriteString("t", check.Title);
            json.WriteString("g", check.Group.Title());
            json.WriteBoolean("score", check.CountsInScore);
            json.WriteString("from", check.From.Key());
            json.WriteString("until", check.Until.Key());
            if (check.Condition is { } condition) json.WriteString("if", condition);
            json.WriteString("why", check.Why);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteSlices(Utf8JsonWriter json, string name, IReadOnlyList<Slice> slices)
    {
        json.WriteStartArray(name);
        foreach (var slice in slices) WriteSlice(json, slice);
        json.WriteEndArray();
    }

    // {"k":"74","t":"Чернігівська область","n":612,"full":380,"avg":0.9312,"c":[[612,600],...]}
    private static void WriteSlice(Utf8JsonWriter json, Slice slice)
    {
        json.WriteStartObject();
        json.WriteString("k", slice.Key);
        json.WriteString("t", slice.Title);
        json.WriteNumber("n", slice.Projects);
        json.WriteNumber("full", slice.Complete);
        json.WriteNumber("avg", Math.Round(slice.AverageScore, 4));
        json.WriteStartArray("c");
        foreach (var tally in slice.Checks)
        {
            json.WriteStartArray();
            json.WriteNumberValue(tally.Applicable);
            json.WriteNumberValue(tally.Filled);
            json.WriteEndArray();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    // [ЄДРПОУ або null, назва, відкритих проєктів, середня наповненість]
    private static void WriteOrganisations(Utf8JsonWriter json, IReadOnlyList<OrganisationStats> organisations)
    {
        json.WriteStartArray("orgs");
        foreach (var organisation in organisations)
        {
            json.WriteStartArray();
            if (organisation.Edrpou is { } edrpou) json.WriteStringValue(edrpou);
            else json.WriteNullValue();
            json.WriteStringValue(organisation.Slice.Title);
            json.WriteNumberValue(organisation.Slice.Projects);
            json.WriteNumberValue(Math.Round(organisation.Slice.AverageScore, 4));
            json.WriteEndArray();
        }
        json.WriteEndArray();
    }

    // [код без "DREAM-UA-", назва, організація, область, стадія, сектор, дата зміни, доречні, заповнені]
    // організація, область, стадія і сектор це індекси в orgs, regions, stages, sectors
    private static void WriteProjects(Utf8JsonWriter json, Summary summary)
    {
        var organisations = Index(summary.Organisations.Select(o => o.Slice.Key));
        var regions = Index(summary.Regions.Select(r => r.Key));
        var stages = Index(summary.Stages.Select(s => s.Key));
        var sectors = Index(summary.Sectors.Select(s => s.Key));

        json.WriteStartArray("p");
        foreach (var result in summary.Open)
        {
            var card = result.Card;
            json.WriteStartArray();
            json.WriteStringValue(ShortCode(card.Code));
            json.WriteStringValue(Truncate(card.Title, MaxTitleLength));
            json.WriteNumberValue(organisations[Organisations.KeyOf(card.Organisation)]);
            json.WriteNumberValue(regions[result.RegionKey]);
            json.WriteNumberValue(stages[card.Stage.Key()]);
            json.WriteNumberValue(sectors[Summary.SectorKey(card)]);
            json.WriteStringValue(card.Updated.UtcDateTime.ToString("yyyy-MM-dd"));
            json.WriteNumberValue(result.Applicable);
            json.WriteNumberValue(result.Filled);
            json.WriteEndArray();
        }
        json.WriteEndArray();
    }

    private static Dictionary<string, int> Index(IEnumerable<string> keys) =>
        keys.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i);

    public static string ShortCode(string code) =>
        code.StartsWith(CodePrefix, StringComparison.Ordinal) ? code[CodePrefix.Length..] : code;

    /// <summary>Обрізає довгу назву до <paramref name="max"/> знаків разом із трикрапкою.</summary>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = max - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }
}
