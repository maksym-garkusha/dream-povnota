namespace DreamPovnota.Cards;

public enum Stage
{
    Idea,
    Defined,
    Appraised,
    Implementation,
    Completed,
    Closed,
    Unknown,
}

public static class Stages
{
    /// <summary>
    /// Стадія за <c>internal.status</c>. Статус пишеться як "група:уточнення",
    /// і для активних, завершених і закритих важлива тільки група.
    /// </summary>
    public static Stage FromStatus(string? status) => status switch
    {
        "pending:initiated" => Stage.Idea,
        "pending:defined" => Stage.Defined,
        "pending:appraised" => Stage.Appraised,
        _ when HasGroup(status, "active") => Stage.Implementation,
        _ when HasGroup(status, "complete") => Stage.Completed,
        _ when HasGroup(status, "cancelled") || HasGroup(status, "unsuccessful") => Stage.Closed,
        _ => Stage.Unknown,
    };

    /// <summary>Відкриті картки беруть участь у перевірках і розрізах; закриті й невідомі ні.</summary>
    public static bool IsOpen(this Stage stage) => stage is >= Stage.Idea and <= Stage.Completed;

    /// <summary>Короткий ключ для data.json і адреси сторінки.</summary>
    public static string Key(this Stage stage) => stage switch
    {
        Stage.Idea => "idea",
        Stage.Defined => "defined",
        Stage.Appraised => "appraised",
        Stage.Implementation => "impl",
        Stage.Completed => "done",
        Stage.Closed => "closed",
        _ => "unknown",
    };

    public static string Title(this Stage stage) => stage switch
    {
        Stage.Idea => "Ідея",
        Stage.Defined => "Визначено",
        Stage.Appraised => "Оцінено",
        Stage.Implementation => "Реалізація",
        Stage.Completed => "Завершено",
        Stage.Closed => "Закрито",
        _ => "Невідомий статус",
    };

    private static bool HasGroup(string? status, string group) =>
        status is not null
        && status.StartsWith(group, StringComparison.Ordinal)
        && (status.Length == group.Length || status[group.Length] == ':');
}
