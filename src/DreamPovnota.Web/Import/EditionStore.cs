namespace DreamPovnota.Web.Import;

/// <summary>
/// Поточний випуск у пам'яті. Новий випуск підміняє старий одним записом посилання,
/// тому запити бачать або старий, або новий, але ніколи напівготовий.
/// </summary>
public sealed class EditionStore
{
    private LoadedEdition? current;
    private ImportStatus status = new(null, null, null);

    public LoadedEdition? Current => Volatile.Read(ref current);

    public ImportStatus Status => Volatile.Read(ref status);

    /// <summary>Для сторінок, до яких без випуску не пускає <see cref="Web.EditionGate"/>.</summary>
    public LoadedEdition Require() =>
        Current ?? throw new InvalidOperationException("Випуск даних ще не завантажено");

    public void Publish(LoadedEdition edition, DateTimeOffset at)
    {
        Volatile.Write(ref current, edition);
        Volatile.Write(ref status, new ImportStatus(at, at, null));
    }

    public void Checked(DateTimeOffset at) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastError = null });

    public void Failed(DateTimeOffset at, string error) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastError = error });
}

/// <param name="LastLoaded">Коли востаннє завантажено новий випуск.</param>
/// <param name="LastChecked">Коли востаннє питали джерело, разом з відповідями 304.</param>
/// <param name="LastError">Чому не вдалося останнього разу; null, якщо вдалося.</param>
public sealed record ImportStatus(DateTimeOffset? LastLoaded, DateTimeOffset? LastChecked, string? LastError);
