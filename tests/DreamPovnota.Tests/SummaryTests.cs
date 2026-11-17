using DreamPovnota.Cards;
using DreamPovnota.Stats;
using static DreamPovnota.Tests.TestCards;

namespace DreamPovnota.Tests;

public class SummaryTests
{
    private static Summary Build(params ProjectCard[] cards) => Summary.Build(cards, [], AsOf);

    [Fact]
    public void Closed_and_unknown_cards_are_counted_but_not_checked()
    {
        var summary = Build(
            Full(Stage.Idea, "p1"),
            Full(Stage.Closed, "p2"),
            Full(Stage.Unknown, "p3") with { Status = "draft" });

        Assert.Equal(3, summary.InCache);
        Assert.Equal(1, summary.Closed);
        Assert.Equal(new Dictionary<string, int> { ["draft"] = 1 }, summary.UnknownStatuses);
        Assert.Equal(["p1"], summary.Open.Select(r => r.Card.Id));
    }

    [Fact]
    public void Stage_slice_has_count_average_and_complete_cards()
    {
        var summary = Build(
            Full(Stage.Idea, "p1"),
            Full(Stage.Idea, "p2") with { SectorCode = null, DocumentCount = 0 });

        var idea = summary.Stages.Single(s => s.Key == "idea");

        Assert.Equal(2, idea.Projects);
        Assert.Equal(1, idea.Complete);
        Assert.Equal(0.75, idea.AverageScore);
        Assert.Equal(new CheckTally(2, 1), idea["sector"]);
        Assert.Equal(0, summary.Stages.Single(s => s.Key == "done").Projects);
    }

    [Fact]
    public void Check_tally_counts_only_cards_where_the_check_applies()
    {
        var summary = Build(Full(Stage.Idea, "p1"), Full(Stage.Appraised, "p2") with { CostEstimate = 0 });

        Assert.Equal(new CheckTally(1, 0), summary.All["cost"]);
        Assert.Equal(new CheckTally(2, 2), summary.All["description"]);
    }

    [Fact]
    public void Regions_are_sorted_by_name_with_several_and_unknown_last()
    {
        var summary = Build(
            Full(Stage.Idea, "p1") with { Katottg = [] },
            Full(Stage.Idea, "p2") with { Katottg = ["UA74060050020095773", "UA05000000000010236"] },
            Full(Stage.Idea, "p3") with { Katottg = ["UA74060050020095773"] },
            Full(Stage.Idea, "p4") with { Katottg = ["UA05000000000010236"] });

        Assert.Equal(["05", "74", "multi", "none"], summary.Regions.Select(r => r.Key));
        Assert.Equal("Вінницька область", summary.Regions[0].Title);
    }

    [Fact]
    public void Card_without_sector_goes_to_not_specified()
    {
        var summary = Build(Full(Stage.Idea, "p1"), Full(Stage.Idea, "p2") with { SectorCode = null });

        Assert.Equal(["E", "none"], summary.Sectors.Select(s => s.Key));
        Assert.Equal(Sectors.NotSpecified, summary.Sectors[1].Title);
    }

    [Fact]
    public void Organisation_takes_its_name_from_the_latest_card()
    {
        var older = Full(Stage.Idea, "p1");
        var newer = Full(Stage.Idea, "p2") with
        {
            Updated = AsOf.AddDays(-1),
            Parties = [new Party(PartyKind.Organisation, "UA-EDR", "04412567", "ПОНОРНИЦЬКА СЕЛИЩНА РАДА", ["projectAuthor"])],
        };

        var organisation = Assert.Single(Build(older, newer).Organisations);

        Assert.Equal("04412567", organisation.Edrpou);
        Assert.Equal("ПОНОРНИЦЬКА СЕЛИЩНА РАДА", organisation.Slice.Title);
        Assert.Equal(2, organisation.Slice.Projects);
    }

    [Fact]
    public void Sole_proprietors_share_one_group_without_code()
    {
        var person = new Party(PartyKind.SoleProprietor, "UA-IPN", null, null, ["projectAuthor"]);

        var summary = Build(
            Full(Stage.Idea, "p1") with { Parties = [person] },
            Full(Stage.Idea, "p2") with { Parties = [person] });

        var group = Assert.Single(summary.Organisations);
        Assert.Null(group.Edrpou);
        Assert.Equal(Organisations.SoleProprietorTitle, group.Slice.Title);
    }

    [Fact]
    public void Seeking_funding_counts_english_titles_separately()
    {
        var seeking = Full(Stage.Implementation, "p1") with { FundingTotal = 0, FundingCommitted = 0, ContractingProcesses = 0 };

        var summary = Build(seeking, seeking with { Id = "p2", HasEnglishTitle = false }, Full(Stage.Implementation, "p3"));

        Assert.Equal(2, summary.SeekingFunding);
        Assert.Equal(1, summary.SeekingFundingWithEnglishTitle);
    }

    [Fact]
    public void Codes_report_invalid_check_digits_and_tax_numbers_in_the_edr_field()
    {
        var taxNumber = new Party(PartyKind.SoleProprietor, "UA-EDR", null, null, ["contractor"]);
        var card = Full(Stage.Idea, "p1", edrpou: "42750838") with
        {
            Parties = [new Party(PartyKind.Organisation, "UA-EDR", "42750838", "РАДА", ["projectAuthor"]), taxNumber],
        };

        var codes = Build(card, Full(Stage.Idea, "p2")).Codes;

        Assert.Equal(2, codes.DistinctEdrpou);
        Assert.Equal(["42750838"], codes.InvalidEdrpou);
        Assert.Equal((1, 1), (codes.TenDigitParties, codes.TenDigitProjects));
    }

    [Fact]
    public void Open_cards_are_sorted_by_title()
    {
        var summary = Build(
            Full(Stage.Idea, "p1") with { Title = "Ремонт мосту" },
            Full(Stage.Idea, "p2") with { Title = "Будівництво укриття" },
            Full(Stage.Idea, "p3") with { Title = "Їдальня ліцею" },
            Full(Stage.Idea, "p4") with { Title = "----- Аварійний ремонт" });

        Assert.Equal(["p4", "p2", "p3", "p1"], summary.Open.Select(r => r.Card.Id));
    }

    [Theory]
    [InlineData("DREAM-UA-190424-A10FC799", 2024, 4, 19)]
    [InlineData("DREAM-UA-050923-9C7CBFF0", 2023, 9, 5)]
    public void Code_carries_the_creation_date(string code, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), Summary.CodeDate(code));
    }

    [Fact]
    public void Code_without_a_date_gives_null()
    {
        Assert.Null(Summary.CodeDate("DREAM-UA-999999-X"));
        Assert.Null(Summary.CodeDate("x"));
    }
}
