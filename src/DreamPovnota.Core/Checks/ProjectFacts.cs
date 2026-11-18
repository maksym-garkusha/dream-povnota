using DreamPovnota.Cards;
using DreamPovnota.Geo;

namespace DreamPovnota.Checks;

/// <summary>
/// Картка проєкту разом з тим, що перевіркам треба взяти з його об'єктів,
/// і віком картки на дату зрізу.
/// </summary>
/// <param name="Katottg">Коди місця: з <c>approaches</c>, а якщо там порожньо, з об'єктів.</param>
/// <param name="DocumentCount">Документи проєкту, його <c>approaches</c> і всіх пов'язаних об'єктів.</param>
/// <param name="MissingObjects">Скільки пов'язаних об'єктів немає в кеші.</param>
/// <param name="DaysSinceUpdate">Повних днів від останньої зміни до дати зрізу.</param>
public sealed record ProjectFacts(
    ProjectCard Card,
    IReadOnlyList<string> Katottg,
    int DocumentCount,
    int MissingObjects,
    int DaysSinceUpdate)
{
    public static ProjectFacts Of(ProjectCard card, IReadOnlyDictionary<string, ObjectCard> objects, DateTimeOffset asOf)
    {
        var linked = card.ObjectIds.Select(id => objects.GetValueOrDefault(id)).ToList();

        return new ProjectFacts(
            card,
            Regions.ProjectKatottg(card, objects),
            card.DocumentCount + linked.OfType<ObjectCard>().Sum(o => o.DocumentCount),
            linked.Count(o => o is null),
            (int)Math.Floor((asOf - card.Updated).TotalDays));
    }

    /// <summary>Сума нескасованих джерел фінансування більша за нуль.</summary>
    public bool HasFunding => Card.FundingTotal > 0;

    /// <summary>Сума джерел зі статусом committed більша за нуль.</summary>
    public bool HasCommittedFunding => Card.FundingCommitted > 0;

    /// <summary>Є хоча б один нескасований контрактний процес (закупівля чи договір).</summary>
    public bool HasProcurement => Card.ContractingProcesses > 0;

    /// <summary>
    /// Схвалений проєкт, у якого ще немає ні грошей, ні закупівель. Порожні поля
    /// фінансування для нього нормальні: він саме їх і шукає.
    /// </summary>
    public bool SeeksFunding => Card.Stage == Stage.Implementation && !HasFunding && !HasProcurement;
}
