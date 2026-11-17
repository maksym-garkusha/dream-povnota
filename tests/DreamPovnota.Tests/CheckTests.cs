using DreamPovnota.Cards;
using DreamPovnota.Checks;
using static DreamPovnota.Tests.TestCards;

namespace DreamPovnota.Tests;

public class CheckTests
{
    private static ObjectCard Object(string id, int documents, params string[] katottg) =>
        new(id, AsOf, katottg, documents);

    [Fact]
    public void Full_card_on_every_open_stage_is_complete()
    {
        foreach (var stage in new[] { Stage.Idea, Stage.Defined, Stage.Appraised, Stage.Implementation, Stage.Completed })
            Assert.True(Evaluate(Full(stage)).IsComplete, stage.ToString());
    }

    [Fact]
    public void Idea_without_object_location_fails_location_check()
    {
        var card = Full(Stage.Idea) with { Katottg = [], ObjectIds = ["o1"] };

        var result = Evaluate(card, Object("o1", 1));

        Assert.True(result.Applies("location"));
        Assert.False(result.IsFilled("location"));
    }

    [Fact]
    public void Idea_takes_location_from_its_object()
    {
        var card = Full(Stage.Idea) with { Katottg = [], ObjectIds = ["o1"] };

        var result = Evaluate(card, Object("o1", 0, "UA59100190010090128"));

        Assert.True(result.IsFilled("location"));
        Assert.Equal("59", result.RegionKey);
    }

    [Theory]
    [InlineData(TextState.Stub)]
    [InlineData(TextState.TooShort)]
    [InlineData(TextState.Empty)]
    public void Description_stub_short_or_empty_is_not_filled(TextState state)
    {
        var result = Evaluate(Full(Stage.Appraised) with { DescriptionState = state });

        Assert.False(result.IsFilled("description"));
    }

    [Fact]
    public void Sector_needs_a_world_bank_code()
    {
        Assert.False(Evaluate(Full(Stage.Idea) with { SectorCode = null }).IsFilled("sector"));
        Assert.False(Evaluate(Full(Stage.Idea) with { SectorCode = "" }).IsFilled("sector"));
    }

    [Fact]
    public void Documents_of_linked_objects_count_for_the_project()
    {
        var card = Full(Stage.Idea) with { DocumentCount = 0, ObjectIds = ["o1"] };

        Assert.False(Evaluate(card, Object("o1", 0)).IsFilled("documents"));
        Assert.True(Evaluate(card, Object("o1", 3)).IsFilled("documents"));
    }

    [Fact]
    public void Object_missing_from_the_cache_is_counted()
    {
        var card = Full(Stage.Idea) with { ObjectIds = ["o1", "o2"] };

        Assert.Equal(1, Evaluate(card, Object("o1", 0)).MissingObjects);
    }

    [Fact]
    public void Duration_and_purpose_are_not_applicable_to_an_idea()
    {
        var result = Evaluate(Full(Stage.Idea) with { DurationMonths = null, GoalState = TextState.Empty });

        Assert.False(result.Applies("duration"));
        Assert.False(result.Applies("purpose"));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Zero_duration_is_not_filled()
    {
        Assert.False(Evaluate(Full(Stage.Defined) with { DurationMonths = 0 }).IsFilled("duration"));
    }

    [Fact]
    public void Purpose_stub_is_not_filled()
    {
        Assert.False(Evaluate(Full(Stage.Defined) with { GoalState = TextState.Stub }).IsFilled("purpose"));
    }

    [Fact]
    public void Cost_applies_from_the_appraised_stage()
    {
        Assert.False(Evaluate(Full(Stage.Defined) with { CostEstimate = 0 }).Applies("cost"));

        var result = Evaluate(Full(Stage.Appraised) with { CostEstimate = 0 });
        Assert.True(result.Applies("cost"));
        Assert.False(result.IsFilled("cost"));
    }

    [Fact]
    public void Funding_applies_only_when_there_is_a_procurement()
    {
        var withoutProcurement = Full(Stage.Implementation) with { ContractingProcesses = 0, FundingTotal = 0, FundingCommitted = 0 };
        var withProcurement = Full(Stage.Implementation) with { FundingTotal = 0, FundingCommitted = 0 };

        Assert.False(Evaluate(withoutProcurement).Applies("funding"));
        Assert.True(Evaluate(withProcurement).Applies("funding"));
        Assert.False(Evaluate(withProcurement).IsFilled("funding"));
    }

    [Fact]
    public void Funding_check_is_not_applicable_before_implementation()
    {
        Assert.False(Evaluate(Full(Stage.Appraised)).Applies("funding"));
    }

    [Fact]
    public void Indicative_funding_does_not_make_procurement_applicable()
    {
        var card = Full(Stage.Implementation) with { FundingCommitted = 0, FundingIndicative = 500m, ContractingProcesses = 0 };

        Assert.False(Evaluate(card).Applies("procurement"));
    }

    [Fact]
    public void Committed_funding_without_procurement_misses_procurement()
    {
        var result = Evaluate(Full(Stage.Completed) with { ContractingProcesses = 0 });

        Assert.True(result.Applies("procurement"));
        Assert.False(result.IsFilled("procurement"));
    }

    [Theory]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void Card_counts_as_fresh_for_365_full_days(int days, bool fresh)
    {
        // ще пів дня зверху: рахуються тільки повні дні
        var card = Full(Stage.Implementation) with { Updated = AsOf.AddDays(-days).AddHours(-12) };

        Assert.Equal(fresh, Evaluate(card).IsFilled("fresh"));
    }

    [Fact]
    public void Fresh_does_not_apply_to_completed_projects()
    {
        Assert.False(Evaluate(Full(Stage.Completed) with { Updated = AsOf.AddYears(-3) }).Applies("fresh"));
    }

    [Fact]
    public void Fresh_does_not_apply_to_a_project_without_money_or_procurement()
    {
        var card = Full(Stage.Implementation) with
        {
            FundingTotal = 0,
            FundingCommitted = 0,
            ContractingProcesses = 0,
            Updated = AsOf.AddYears(-2),
        };

        var result = Evaluate(card);

        Assert.False(result.Applies("fresh"));
        Assert.True(result.SeeksFunding);
    }

    [Fact]
    public void Source_with_zero_amount_still_seeks_funding()
    {
        var card = Full(Stage.Implementation) with
        {
            FundingSourceCount = 1,
            FundingTotal = 0,
            FundingCommitted = 0,
            ContractingProcesses = 0,
        };

        Assert.True(Evaluate(card).SeeksFunding);
    }

    [Fact]
    public void Only_implementation_can_seek_funding()
    {
        var card = Full(Stage.Appraised) with { FundingTotal = 0, FundingCommitted = 0, ContractingProcesses = 0 };

        Assert.False(Evaluate(card).SeeksFunding);
    }

    [Theory]
    [InlineData(Stage.Closed)]
    [InlineData(Stage.Unknown)]
    public void Closed_or_unknown_card_has_no_applicable_checks(Stage stage)
    {
        var result = Evaluate(Full(stage));

        Assert.Equal(0, result.Applicable);
        Assert.Equal(0, result.ScoreApplicable);
    }

    [Fact]
    public void English_fields_do_not_count_in_the_score()
    {
        var result = Evaluate(Full(Stage.Idea) with { HasEnglishTitle = false, HasEnglishDescription = false });

        Assert.Equal(1.0, result.Score);
        Assert.Equal(["title-en", "description-en"], result.Missing.Select(c => c.Code));
    }

    [Fact]
    public void Score_is_filled_out_of_applicable()
    {
        // ідея: чотири основні перевірки, з них дві не заповнено
        var result = Evaluate(Full(Stage.Idea) with { SectorCode = null, DocumentCount = 0 });

        Assert.Equal((2, 4), (result.ScoreFilled, result.ScoreApplicable));
        Assert.Equal(0.5, result.Score);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Active_fixture_has_every_check_applicable_and_filled()
    {
        // DREAM-UA-190424-A10FC799: закупівля і підтверджене фінансування є, остання зміна 15.04.2026
        var card = CardParser.ParseProject(Fixture.Text("project-active.json"));
        var obj = CardParser.ParseObject(Fixture.Text("object-active.json"));

        var result = Evaluate(card, obj);

        Assert.Equal(CheckList.All.Count, System.Numerics.BitOperations.PopCount((uint)result.Applicable));
        Assert.Equal(result.Applicable, result.Filled);
        Assert.False(result.SeeksFunding);
    }

    [Fact]
    public void Appraised_fixture_misses_description_sector_duration_and_purpose()
    {
        var card = CardParser.ParseProject(Fixture.Text("project-appraised.json"));

        var missing = Evaluate(card).Missing.Select(c => c.Code).Where(c => !c.EndsWith("-en")).ToList();

        Assert.Equal(["description", "sector", "duration", "purpose"], missing);
    }

    [Fact]
    public void Check_codes_are_unique_and_fit_in_an_int_mask()
    {
        Assert.Equal(CheckList.All.Count, CheckList.All.Select(c => c.Code).Distinct().Count());
        Assert.True(CheckList.All.Count < 31);
    }
}
