using System.Numerics;
using DreamPovnota.Cards;

namespace DreamPovnota.Checks;

/// <summary>Що доречно і що заповнено в одній картці.</summary>
/// <param name="RegionKey">Дві цифри області, "multi" або "none".</param>
/// <param name="Applicable">Біти доречних перевірок у порядку <see cref="CheckList.All"/>.</param>
/// <param name="Filled">Біти заповнених; завжди підмножина доречних.</param>
/// <param name="MissingObjects">Скільки пов'язаних об'єктів не знайшлося в кеші.</param>
public sealed record CardResult(
    ProjectCard Card,
    string RegionKey,
    int Applicable,
    int Filled,
    bool SeeksFunding,
    int MissingObjects)
{
    private static readonly int ScoreMask = CheckList.All
        .Select((check, i) => check.CountsInScore ? 1 << i : 0)
        .Aggregate(0, (mask, bit) => mask | bit);

    public bool Applies(int checkIndex) => (Applicable & (1 << checkIndex)) != 0;

    public bool IsFilled(int checkIndex) => (Filled & (1 << checkIndex)) != 0;

    public bool Applies(string code) => Applies(CheckList.IndexOf(code));

    public bool IsFilled(string code) => IsFilled(CheckList.IndexOf(code));

    /// <summary>Доречних перевірок без англійських.</summary>
    public int ScoreApplicable => BitOperations.PopCount((uint)(Applicable & ScoreMask));

    public int ScoreFilled => BitOperations.PopCount((uint)(Filled & ScoreMask));

    /// <summary>Наповненість від 0 до 1. У відкритої картки доречні щонайменше чотири перевірки.</summary>
    public double Score => ScoreApplicable == 0 ? 0 : (double)ScoreFilled / ScoreApplicable;

    public bool IsComplete => ScoreApplicable > 0 && ScoreFilled == ScoreApplicable;

    /// <summary>Доречні, але не заповнені перевірки, разом з англійськими.</summary>
    public IEnumerable<Check> Missing =>
        CheckList.All.Where((_, i) => Applies(i) && !IsFilled(i));
}
