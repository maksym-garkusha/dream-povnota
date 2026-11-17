using DreamPovnota.Cards;
using DreamPovnota.Geo;

namespace DreamPovnota.Tests;

public class RegionsTests
{
    [Theory]
    [InlineData("UA74060050020095773", "Чернігівська область")]
    [InlineData("UA71080490000022110", "Черкаська область")]
    [InlineData("UA80000000000093317", "місто Київ")]
    public void Region_is_read_from_the_first_two_digits(string code, string region)
    {
        Assert.Equal(region, Regions.Name(Regions.RegionCode(code)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UA7406005002009577")]     // 16 цифр
    [InlineData("ua74060050020095773")]
    [InlineData("UA7406005002009577X")]
    [InlineData("UA99060050020095773")]    // області 99 немає
    public void Code_that_is_not_katottg_has_no_region(string? code)
    {
        Assert.Null(Regions.RegionCode(code));
    }

    [Fact]
    public void Codes_from_one_region_give_that_region()
    {
        Assert.Equal("Чернігівська область", Regions.Of(["UA74060050020095773", "UA74040270010056465"]));
    }

    [Fact]
    public void Codes_from_different_regions_give_several_regions()
    {
        Assert.Equal(Regions.Several, Regions.Of(["UA74060050020095773", "UA71080490000022110"]));
    }

    [Fact]
    public void No_codes_or_only_broken_codes_give_not_specified()
    {
        Assert.Equal(Regions.NotSpecified, Regions.Of([]));
        Assert.Equal(Regions.NotSpecified, Regions.Of(["UA00000000000000000", "123"]));
    }

    [Fact]
    public void Broken_code_next_to_good_one_is_skipped()
    {
        Assert.Equal("місто Київ", Regions.Of(["UA80000000000093317", "UA9"]));
    }

    [Fact]
    public void Project_location_comes_from_approaches_first()
    {
        var card = CardParser.ParseProject(Fixture.Text("project-active.json"));
        var other = new ObjectCard(card.ObjectIds[0], DateTimeOffset.UnixEpoch, ["UA71080490000022110"], 0);

        Assert.Equal("Чернігівська область", Regions.Of(card, Objects(other)));
    }

    [Fact]
    public void Project_without_approaches_takes_location_from_its_objects()
    {
        var card = CardParser.ParseProject(Fixture.Text("project-defined.json"));
        var obj = CardParser.ParseObject(Fixture.Text("object-defined.json"));

        Assert.Equal(["UA12140330010029935"], Regions.ProjectKatottg(card, Objects(obj)));
        Assert.Equal("Дніпропетровська область", Regions.Of(card, Objects(obj)));
    }

    [Fact]
    public void Project_without_codes_anywhere_is_not_specified()
    {
        var card = CardParser.ParseProject(Fixture.Text("project-idea.json"));

        Assert.Equal(Regions.NotSpecified, Regions.Of(card, Objects()));
    }

    [Fact]
    public void Region_key_is_two_digits_multi_or_none()
    {
        Assert.Equal("74", Regions.KeyOf(["UA74060050020095773"]));
        Assert.Equal(Regions.SeveralKey, Regions.KeyOf(["UA74060050020095773", "UA71080490000022110"]));
        Assert.Equal(Regions.NotSpecifiedKey, Regions.KeyOf([]));
    }

    [Theory]
    [InlineData("74", "Чернігівська область")]
    [InlineData(Regions.SeveralKey, Regions.Several)]
    [InlineData(Regions.NotSpecifiedKey, Regions.NotSpecified)]
    public void Region_title_comes_from_the_key(string key, string title)
    {
        Assert.Equal(title, Regions.Title(key));
    }

    private static Dictionary<string, ObjectCard> Objects(params ObjectCard[] objects) =>
        objects.ToDictionary(o => o.Id);
}
