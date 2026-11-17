namespace DreamPovnota.Cards;

/// <summary>Об'єкт, до якого належить проєкт (школа, міст, котельня). Нам з нього треба місце і документи.</summary>
public record ObjectCard(
    string Id,
    DateTimeOffset Updated,
    IReadOnlyList<string> Katottg,
    int DocumentCount);
