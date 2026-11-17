using System.Text;
using System.Text.Json;
using DreamPovnota.Cards;
using DreamPovnota.Checks;
using DreamPovnota.Export;
using DreamPovnota.Stats;
using DreamPovnota.Store;
using static DreamPovnota.Tests.TestCards;

namespace DreamPovnota.Tests;

public class DataExportTests
{
    private static readonly string[] Projects =
    [
        "project-active.json", "project-active-no-contracting.json", "project-appraised.json",
        "project-cancelled.json", "project-complete.json", "project-defined.json", "project-english.json",
        "project-idea.json", "project-individual.json", "project-unsuccessful.json",
    ];

    private static Summary FixtureSummary(SyncInfo? sync = null) => Summary.Build(
        Projects.Select(f => CardParser.ParseProject(Fixture.Text(f))).ToList(),
        new[] { "object-active.json", "object-defined.json", "object-idea.json" }
            .Select(f => CardParser.ParseObject(Fixture.Text(f))),
        AsOf,
        sync);

    private static string Export(Summary summary)
    {
        using var stream = new MemoryStream();
        DataExport.Write(summary, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonElement Parse(Summary summary) => JsonDocument.Parse(Export(summary)).RootElement;

    [Fact]
    public void Export_has_no_personal_data()
    {
        string json = Export(FixtureSummary());

        Assert.DoesNotContain("1234567899", json);
        Assert.DoesNotContain("Вигадан", json);
    }

    [Fact]
    public void Closed_projects_are_counted_but_not_listed()
    {
        var root = Parse(FixtureSummary());
        var codes = root.GetProperty("p").EnumerateArray().Select(p => p[0].GetString()).ToList();

        Assert.Equal(2, root.GetProperty("counts").GetProperty("closed").GetInt32());
        Assert.Equal(8, codes.Count);
        Assert.Contains("190424-A10FC799", codes);
        Assert.DoesNotContain(codes, c => c!.StartsWith("DREAM-UA-"));
    }

    [Fact]
    public void Project_row_points_into_the_dictionaries()
    {
        var root = Parse(FixtureSummary());
        var row = root.GetProperty("p").EnumerateArray().Single(p => p[0].GetString() == "190424-A10FC799");

        var organisation = root.GetProperty("orgs")[row[2].GetInt32()];
        Assert.Equal("04412567", organisation[0].GetString());
        Assert.Equal("74", root.GetProperty("regions")[row[3].GetInt32()].GetProperty("k").GetString());
        Assert.Equal("impl", root.GetProperty("stages")[row[4].GetInt32()].GetProperty("k").GetString());
        Assert.Equal("E", root.GetProperty("sectors")[row[5].GetInt32()].GetProperty("k").GetString());
        Assert.Equal("2026-04-15", row[6].GetString());
        Assert.Equal(row[7].GetInt32(), row[8].GetInt32());
        Assert.Equal(0, row[9].GetInt32());
    }

    [Fact]
    public void Project_seeking_funding_is_flagged()
    {
        var seeking = Full(Stage.Implementation) with { FundingTotal = 0, FundingCommitted = 0, ContractingProcesses = 0 };

        var row = Parse(Summary.Build([seeking], [], AsOf)).GetProperty("p")[0];

        Assert.Equal(1, row[9].GetInt32());
    }

    [Fact]
    public void Check_masks_follow_the_order_of_the_checks_list()
    {
        var root = Parse(FixtureSummary());
        var codes = root.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("c").GetString());

        Assert.Equal(CheckList.All.Select(c => c.Code), codes);
    }

    [Fact]
    public void Slice_has_a_pair_of_counts_per_check()
    {
        var all = Parse(FixtureSummary()).GetProperty("all");

        Assert.Equal(8, all.GetProperty("n").GetInt32());
        Assert.Equal(CheckList.All.Count, all.GetProperty("c").GetArrayLength());
        Assert.Equal(8, all.GetProperty("c")[0][0].GetInt32());
    }

    [Fact]
    public void Counts_without_sync_info_leave_feed_unknown()
    {
        var counts = Parse(FixtureSummary()).GetProperty("counts");

        Assert.Equal(JsonValueKind.Null, counts.GetProperty("feed").ValueKind);
        Assert.Equal(10, counts.GetProperty("cache").GetInt32());
    }

    [Fact]
    public void Counts_with_sync_info_show_feed_and_unparsed()
    {
        var sync = new SyncInfo(AsOf, 12, 0, 1, ["x"], 0);

        var counts = Parse(FixtureSummary(sync)).GetProperty("counts");

        Assert.Equal(12, counts.GetProperty("feed").GetInt32());
        Assert.Equal(1, counts.GetProperty("unparsed").GetInt32());
    }

    [Fact]
    public void Long_title_is_cut_to_160_characters()
    {
        var card = Full(Stage.Idea) with { Title = new string('а', 300) };

        var title = Parse(Summary.Build([card], [], AsOf)).GetProperty("p")[0][1].GetString()!;

        Assert.Equal(DataExport.MaxTitleLength, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Cyrillic_is_written_as_is_to_keep_the_file_small()
    {
        string json = Export(Summary.Build([Full(Stage.Idea)], [], AsOf));

        Assert.Contains("Капітальний ремонт школи", json);
        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public async Task Export_writes_data_json_into_the_site_directory()
    {
        using var dir = new TempDir();

        await DataExport.WriteAsync(Summary.Build([Full(Stage.Idea)], [], AsOf), dir.Path);

        Assert.Equal([DataExport.FileName], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }
}
