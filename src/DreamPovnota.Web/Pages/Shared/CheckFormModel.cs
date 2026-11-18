namespace DreamPovnota.Web.Pages;

/// <param name="Value">Що було в полі, щоб не вводити заново після помилки.</param>
public sealed record CheckFormModel(string? Value, string? Error);
