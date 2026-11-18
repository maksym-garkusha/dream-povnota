using DreamPovnota.Checks;

namespace DreamPovnota.Stats;

/// <summary>Скільки карток, де перевірка доречна, і в скількох із них поле заповнено.</summary>
public sealed record CheckTally(int Applicable, int Filled)
{
    public double Share => Applicable == 0 ? 0 : (double)Filled / Applicable;
}

/// <summary>
/// Група карток одного розрізу: стадія, область, сектор чи організація.
/// </summary>
/// <param name="Key">Ключ для data.json і адреси сторінки: "impl", "74", "E", ЄДРПОУ.</param>
/// <param name="Complete">Скільки карток мають усе, що доречно на їхній стадії.</param>
/// <param name="AverageScore">Середня наповненість карток від 0 до 1.</param>
/// <param name="Checks">По одному на перевірку, у порядку <see cref="CheckList.All"/>.</param>
public sealed record Slice(
    string Key,
    string Title,
    int Projects,
    int Complete,
    double AverageScore,
    IReadOnlyList<CheckTally> Checks)
{
    public static Slice Of(string key, string title, IReadOnlyCollection<CardResult> cards)
    {
        var checks = CheckList.All
            .Select((_, i) => new CheckTally(cards.Count(c => c.Applies(i)), cards.Count(c => c.IsFilled(i))))
            .ToList();

        return new Slice(
            key,
            title,
            cards.Count,
            cards.Count(c => c.IsComplete),
            cards.Count == 0 ? 0 : cards.Average(c => c.Score),
            checks);
    }

    public CheckTally this[string code] => Checks[CheckList.IndexOf(code)];
}
