using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.Http;

namespace DreamPovnota.Web.Tests;

public class CardFilterTests
{
    private static async Task<(Dictionary<string, string[]> Errors, CardFilter Filter, Import.LoadedEdition Edition)> Parse(
        string query, bool api = false)
    {
        var edition = await Fixtures.LoadedAsync();
        var parsed = new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query));
        var errors = CardFilter.TryParse(parsed, edition, api, out var filter);
        return (errors, filter, edition);
    }

    [Fact]
    public async Task Empty_query_gives_every_open_card()
    {
        var (errors, filter, edition) = await Parse("");

        Assert.Empty(errors);
        Assert.Equal(edition.Edition.Cards.Count, filter.Apply(edition).Count);
    }

    [Fact]
    public async Task Region_filter_keeps_only_that_region()
    {
        var edition = await Fixtures.LoadedAsync();
        string region = edition.Edition.Regions[0].Key;

        var (errors, filter, _) = await Parse("region=" + region);

        Assert.Empty(errors);
        Assert.All(filter.Apply(edition), i => Assert.Equal(0, edition.Edition.Cards[i].Region));
    }

    [Fact]
    public async Task Missing_filter_keeps_cards_where_the_field_applies_but_is_empty()
    {
        var (errors, filter, edition) = await Parse("missing=title-en");
        int bit = 1 << edition.CheckIndex["title-en"];

        Assert.Empty(errors);
        Assert.All(filter.Apply(edition), i =>
        {
            var card = edition.Edition.Cards[i];
            Assert.True((card.Applicable & bit) != 0 && (card.Filled & bit) == 0);
        });
    }

    [Theory]
    [InlineData("region=99", "region")]
    [InlineData("stage=closed", "stage")]
    [InlineData("sector=Z", "sector")]
    [InlineData("missing=salary", "missing")]
    [InlineData("seeks=yes", "seeks")]
    [InlineData("page=0", "page")]
    [InlineData("page=1001", "page")]
    [InlineData("page=1e3", "page")]
    [InlineData("org=00000000", "org")]
    [InlineData("pageSize=10", "pageSize")]
    [InlineData("region=74&region=05", "region")]
    [InlineData("sort=worst", "sort")]
    public async Task Unknown_or_out_of_range_value_is_reported_for_its_parameter(string query, string parameter)
    {
        var (errors, _, _) = await Parse(query);

        Assert.True(errors.ContainsKey(parameter), string.Join(", ", errors.Keys));
    }

    [Fact]
    public async Task Search_longer_than_a_hundred_characters_is_refused()
    {
        var (errors, _, _) = await Parse("q=" + new string('а', 101));

        Assert.Contains("q", errors.Keys);
    }

    [Fact]
    public async Task Page_size_is_allowed_only_in_the_api_and_capped()
    {
        Assert.Empty((await Parse("pageSize=500", api: true)).Errors);
        Assert.Contains("pageSize", (await Parse("pageSize=501", api: true)).Errors.Keys);
    }

    [Fact]
    public async Task Search_finds_a_card_by_its_full_dream_code()
    {
        var edition = await Fixtures.LoadedAsync();
        string code = edition.Edition.Cards[0].Code;

        var (_, filter, _) = await Parse("q=dream-ua-" + code.ToLowerInvariant());

        Assert.Equal([0], filter.Apply(edition));
    }

    [Fact]
    public void Links_keep_the_filter_and_drop_the_first_page_number()
    {
        var filter = CardFilter.Empty with { Region = "74", Missing = "cost", SeeksFunding = true };

        Assert.Equal("?region=74&missing=cost&seeks=1", filter.QueryString(1));
        Assert.Equal("?region=74&missing=cost&seeks=1&page=3", filter.QueryString(3));
    }
}
