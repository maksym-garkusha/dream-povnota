using DreamPovnota.Cards;
using DreamPovnota.Geo;

namespace DreamPovnota.Checks;

/// <summary>
/// Усі перевірки в одному місці. Порядок важливий: за ним складаються бітові
/// маски в data.json, тому нову перевірку дописуємо в кінець своєї групи
/// і разом з нею оновлюємо сторінку.
/// </summary>
public static class CheckList
{
    /// <summary>Картку, яку не змінювали довше, вважаємо давно не оновленою.</summary>
    public const int FreshDays = 365;

    public static IReadOnlyList<Check> All { get; } =
    [
        new("description", "Опис проєкту", CheckGroup.Basics, Stage.Idea,
            "З опису зрозуміло, що саме робитимуть і навіщо.",
            f => f.Card.DescriptionState == TextState.Filled),
        new("location", "Місце (код КАТОТТГ)", CheckGroup.Basics, Stage.Idea,
            "За кодом громади проєкт потрапляє в розріз за областями.",
            f => f.Katottg.Count > 0),
        new("sector", "Сектор", CheckGroup.Basics, Stage.Idea,
            "Без сектору проєкт не видно в зведеннях на зразок «скільки в освіті».",
            f => !string.IsNullOrEmpty(f.Card.SectorCode)),
        new("documents", "Документи або фото", CheckGroup.Basics, Stage.Idea,
            "Документи й фото показують, у якому стані об'єкт і що вже підготовлено.",
            f => f.DocumentCount > 0),

        new("duration", "Тривалість", CheckGroup.Planning, Stage.Defined,
            "Тривалість потрібна, щоб планувати фінансування за роками.",
            f => f.Card.DurationMonths > 0),
        new("purpose", "Мета", CheckGroup.Planning, Stage.Defined,
            "Мета пояснює, яку потребу громади закриває проєкт.",
            f => f.Card.GoalState == TextState.Filled),

        new("cost", "Кошторисна вартість", CheckGroup.Appraisal, Stage.Appraised,
            "Без кошторису не видно, скільки грошей потрібно.",
            f => f.Card.CostEstimate > 0),

        // статус active:approved означає «схвалено», а не «гроші є», тому три
        // перевірки реалізації перевіряють узгодженість картки, а не саму стадію
        new("funding", "Джерело фінансування", CheckGroup.Implementation, Stage.Implementation,
            "Якщо закупівля вже є, гроші на неї звідкись ідуть, і джерело варто вказати.",
            f => f.HasFunding,
            Condition: "якщо є закупівля чи договір",
            AppliesWhen: f => f.HasProcurement),
        new("procurement", "Закупівля або договір", CheckGroup.Implementation, Stage.Implementation,
            "Коли фінансування підтверджено, наступний крок це закупівля або договір.",
            f => f.HasProcurement,
            Condition: "якщо є підтверджене фінансування",
            AppliesWhen: f => f.HasCommittedFunding),
        new("fresh", "Оновлювалась за останній рік", CheckGroup.Implementation, Stage.Implementation,
            "Проєкт, у якому вже рухаються гроші чи договори, має показувати поточний стан.",
            f => f.DaysSinceUpdate <= FreshDays,
            Until: Stage.Implementation,
            Condition: "якщо є фінансування або закупівля",
            AppliesWhen: f => f.HasFunding || f.HasProcurement),

        new("title-en", "Назва англійською", CheckGroup.English, Stage.Idea,
            "Англійську назву бачать іноземні донори й партнери.",
            f => f.Card.HasEnglishTitle),
        new("description-en", "Опис англійською", CheckGroup.English, Stage.Idea,
            "Англійський опис потрібен донорам, які не читають українською.",
            f => f.Card.HasEnglishDescription),
    ];

    public static int IndexOf(string code)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Code == code) return i;
        throw new ArgumentException($"Немає перевірки {code}", nameof(code));
    }

    public static Check Get(string code) => All[IndexOf(code)];

    /// <summary>Які перевірки доречні і які з них заповнено, бітами в порядку <see cref="All"/>.</summary>
    public static CardResult Evaluate(ProjectFacts facts)
    {
        int applicable = 0, filled = 0;
        for (int i = 0; i < All.Count; i++)
        {
            if (!All[i].AppliesTo(facts)) continue;
            applicable |= 1 << i;
            if (All[i].IsFilled(facts)) filled |= 1 << i;
        }
        return new CardResult(facts.Card, Regions.KeyOf(facts.Katottg), applicable, filled, facts.SeeksFunding, facts.MissingObjects);
    }
}
