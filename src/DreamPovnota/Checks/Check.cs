using DreamPovnota.Cards;

namespace DreamPovnota.Checks;

public enum CheckGroup
{
    Basics,
    Planning,
    Appraisal,
    Implementation,

    /// <summary>Рахується окремо і в наповненість картки не входить.</summary>
    English,
}

/// <summary>
/// Одна ознака наповненості картки: яке поле, з якої стадії воно доречне
/// і коли вважаємо його заповненим.
/// </summary>
/// <param name="Code">Короткий код для data.json і адреси сторінки: "cost", "title-en".</param>
/// <param name="From">З якої стадії поле доречне.</param>
/// <param name="Until">До якої стадії включно; для більшості це «Завершено».</param>
/// <param name="Why">Одне речення: навіщо це поле тому, хто читає картку.</param>
/// <param name="Condition">Додаткова умова доречності словами або null.</param>
/// <param name="AppliesWhen">Додаткова умова доречності, крім стадії.</param>
public sealed record Check(
    string Code,
    string Title,
    CheckGroup Group,
    Stage From,
    string Why,
    Func<ProjectFacts, bool> IsFilled,
    Stage Until = Stage.Completed,
    string? Condition = null,
    Func<ProjectFacts, bool>? AppliesWhen = null)
{
    public bool AppliesTo(ProjectFacts facts) =>
        facts.Card.IsOpen
        && facts.Card.Stage >= From
        && facts.Card.Stage <= Until
        && (AppliesWhen?.Invoke(facts) ?? true);

    /// <summary>Чи входить у наповненість картки (англійські поля ні).</summary>
    public bool CountsInScore => Group != CheckGroup.English;
}

public static class CheckGroups
{
    public static string Title(this CheckGroup group) => group switch
    {
        CheckGroup.Basics => "Основне",
        CheckGroup.Planning => "Планування",
        CheckGroup.Appraisal => "Оцінка",
        CheckGroup.Implementation => "Реалізація",
        _ => "Англійською",
    };
}
