namespace DreamPovnota.Web.Import;

/// <summary>Пороги /health/fresh, розділ Freshness у налаштуваннях. Звідки ці числа, сказано в README.</summary>
public sealed class FreshnessLimits
{
    public const string Section = "Freshness";

    /// <summary>Скільки годин може минути з generated поточного випуску, тобто з його збірки в site.yml.</summary>
    public double EditionAgeHours { get; set; } = 36;

    /// <summary>Скільки годин поспіль імпорт може не діставати data.json, поки це вважається перехідним збоєм.</summary>
    public double ImportFailingHours { get; set; } = 3;
}

/// <summary>Тіло /health/fresh.</summary>
/// <param name="Reason">Чому дані не свіжі, одним із кодів <see cref="Freshness"/>; null, якщо свіжі.</param>
/// <param name="LastSuccessfulCheck">Коли джерело востаннє відповіло, див. <see cref="ImportStatus.LastSucceeded"/>.</param>
/// <param name="EditionAgeHours">Скільки годин минуло з generated поточного випуску.</param>
public sealed record FreshnessReport(bool Fresh, string? Reason, DateTimeOffset? LastSuccessfulCheck, double? EditionAgeHours);

/// <summary>
/// Чи свіжі дані, якими зараз відповідає сервіс. /health/ready дає 200, поки є хоч якийсь
/// випуск, навіть коли запобіжник відкинув новий файл чи новий випуск давно не з'являвся.
/// Тут ці випадки дають 503, так само як відсутній випуск і імпорт, що довго не вдається.
/// </summary>
public static class Freshness
{
    public const string NoEdition = "no-edition";
    public const string GuardHeld = "guard-held";
    public const string ImportFailing = "import-failing";
    public const string Stale = "stale";

    /// <remarks>
    /// Причини перевіряються по черзі, і називається перша: відкинутий випуск важливіший
    /// за решту, бо сам не минеться, а старий випуск часто лише наслідок збою імпорту.
    /// </remarks>
    public static FreshnessReport Of(EditionStore store, DateTimeOffset now, FreshnessLimits limits)
    {
        var current = store.Current;
        var succeeded = store.Status.LastSucceeded;
        double? age = current is null ? null : Hours(now - current.Edition.Generated);

        string? reason =
            store.HeldReason is not null ? GuardHeld
            : current is null ? NoEdition
            : succeeded is null || Hours(now - succeeded.Value) > limits.ImportFailingHours ? ImportFailing
            : age > limits.EditionAgeHours ? Stale
            : null;

        return new FreshnessReport(reason is null, reason, succeeded, age is { } hours ? Math.Round(hours, 1) : null);
    }

    // годинники раннера GitHub і Render можуть розходитися на секунди, тож від'ємний вік рахуємо нулем
    private static double Hours(TimeSpan span) => Math.Max(0, span.TotalHours);
}
