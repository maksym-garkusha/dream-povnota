using System.Text;
using System.Text.Json.Nodes;
using DreamPovnota.Cards;
using DreamPovnota.Export;
using DreamPovnota.Stats;
using static DreamPovnota.Tests.TestCards;

namespace DreamPovnota.Tests;

public class EditionTests
{
    private static readonly string[] Projects =
    [
        "project-active.json", "project-active-no-contracting.json", "project-appraised.json",
        "project-cancelled.json", "project-complete.json", "project-defined.json", "project-english.json",
        "project-idea.json", "project-individual.json", "project-unsuccessful.json",
    ];

    private static Summary FixtureSummary() => Summary.Build(
        Projects.Select(f => CardParser.ParseProject(Fixture.Text(f))).ToList(),
        new[] { "object-active.json", "object-defined.json", "object-idea.json" }
            .Select(f => CardParser.ParseObject(Fixture.Text(f))),
        AsOf);

    private static JsonObject ExportJson()
    {
        using var stream = new MemoryStream();
        DataExport.Write(FixtureSummary(), stream);
        return JsonNode.Parse(stream.ToArray())!.AsObject();
    }

    private static Task<Edition> Read(JsonNode json) =>
        Edition.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString())));

    [Fact]
    public async Task Edition_reads_back_what_the_export_wrote()
    {
        var summary = FixtureSummary();
        var edition = await Read(ExportJson());

        Assert.Equal(summary.AsOf, edition.Generated);
        Assert.Equal(summary.Open.Count, edition.Cards.Count);
        Assert.Equal(summary.Regions.Select(r => r.Key), edition.Regions.Select(r => r.Key));
        Assert.Equal(summary.All.Checks, edition.All.Checks);
        for (int i = 0; i < summary.Open.Count; i++)
        {
            var result = summary.Open[i];
            var card = edition.Cards[i];
            Assert.Equal(DataExport.ShortCode(result.Card.Code), card.Code);
            Assert.Equal(result.Applicable, card.Applicable);
            Assert.Equal(result.Filled, card.Filled);
            Assert.Equal(result.RegionKey, edition.Regions[card.Region].Key);
            Assert.Equal(Organisations.KeyOf(result.Card.Organisation) == Organisations.SoleProprietorKey
                ? "n:" + Organisations.SoleProprietorTitle
                : result.Card.Organisation!.Edrpou, edition.Organisations[card.Organisation].Key);
        }
    }

    [Fact]
    public async Task Organisation_without_code_is_keyed_by_its_title()
    {
        var json = ExportJson();
        json["orgs"]![0]![0] = null;
        json["orgs"]![0]![1] = "ФОНД ВІДНОВЛЕННЯ";

        var edition = await Read(json);

        Assert.Equal("n:ФОНД ВІДНОВЛЕННЯ", edition.Organisations[0].Key);
        Assert.Null(edition.Organisations[0].Edrpou);
    }

    [Fact]
    public async Task Organisation_titles_are_not_cut_and_may_be_longer_than_card_titles()
    {
        var json = ExportJson();
        json["orgs"]![0]![1] = new string('А', 300);

        var edition = await Read(json);

        Assert.Equal(300, edition.Organisations[0].Title.Length);
    }

    [Fact]
    public async Task Card_title_longer_than_two_hundred_characters_is_refused()
    {
        var json = ExportJson();
        json["p"]![0]![1] = new string('А', 201);

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Edition_of_another_version_is_refused()
    {
        var json = ExportJson();
        json["v"] = 2;

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Card_pointing_past_the_region_list_is_refused()
    {
        var json = ExportJson();
        json["p"]![0]![3] = 999;

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Card_filled_outside_its_applicable_checks_is_refused()
    {
        var json = ExportJson();
        json["p"]![0]![7] = 1;
        json["p"]![0]![8] = 3;

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Open_count_that_differs_from_the_cards_is_refused()
    {
        var json = ExportJson();
        json["counts"]!["open"] = 1000;

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Card_code_with_lower_case_letters_is_refused()
    {
        var json = ExportJson();
        json["p"]![0]![0] = "190424-abcdef12";

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(json));
    }

    [Fact]
    public async Task Broken_json_is_reported_as_invalid_data()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Edition.ReadAsync(new MemoryStream("{\"v\":1,"u8.ToArray())));
    }
}
