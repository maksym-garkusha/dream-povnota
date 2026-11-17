using DreamPovnota.Cards;
using DreamPovnota.Checks;

namespace DreamPovnota.Tests;

/// <summary>
/// Картки для тестів правил. <see cref="Full"/> заповнена повністю для своєї стадії,
/// тест прибирає з неї одне поле через <c>with</c> і дивиться, що змінилось.
/// </summary>
public static class TestCards
{
    /// <summary>Та сама дата зрізу, що в еталоні.</summary>
    public static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-11-17T22:00:00Z");

    public static ProjectCard Full(Stage stage, string id = "p1", string edrpou = "04412567") => new()
    {
        Id = id,
        Code = "DREAM-UA-190424-" + id.ToUpperInvariant(),
        Status = stage switch
        {
            Stage.Idea => "pending:initiated",
            Stage.Defined => "pending:defined",
            Stage.Appraised => "pending:appraised",
            Stage.Implementation => "active:approved",
            Stage.Completed => "complete",
            Stage.Closed => "cancelled:duplicate",
            _ => "draft",
        },
        Stage = stage,
        Updated = AsOf.AddDays(-30),
        Title = "Капітальний ремонт школи № 3",
        HasEnglishTitle = true,
        DescriptionState = TextState.Filled,
        DescriptionLength = 120,
        HasEnglishDescription = true,
        GoalState = TextState.Filled,
        DurationMonths = 12,
        Parties = [new Party(PartyKind.Organisation, "UA-EDR", edrpou, "СЕЛИЩНА РАДА", ["projectAuthor"])],
        SectorCode = "ES",
        Katottg = ["UA74060050020095773"],
        DocumentCount = 2,
        CostEstimate = 1_000_000m,
        FundingSourceCount = 1,
        FundingTotal = 1_000_000m,
        FundingCommitted = 1_000_000m,
        ContractingProcesses = 1,
        ContractCount = 1,
    };

    public static CardResult Evaluate(ProjectCard card, params ObjectCard[] objects) =>
        CheckList.Evaluate(ProjectFacts.Of(card, objects.ToDictionary(o => o.Id), AsOf));
}
