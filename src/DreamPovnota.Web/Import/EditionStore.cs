namespace DreamPovnota.Web.Import;

/// <summary>
/// Поточний випуск у пам'яті. Новий випуск підміняє старий одним записом посилання,
/// тому запити бачать або старий, або новий, але ніколи напівготовий.
/// </summary>
public sealed class EditionStore
{
    private LoadedEdition? current;
    private ImportStatus status = new(null, null, null);
    private long generation;

    public LoadedEdition? Current => Volatile.Read(ref current);

    /// <summary>
    /// Росте з кожним новим випуском і знімком історії; входить у ключ кешу відповідей.
    /// Самого очищення кешу за тегом мало: запит, який прочитав дані за мить до зміни,
    /// кладе відповідь у кеш уже після очищення. З поколінням у ключі вона лягає під
    /// старий ключ, і нові запити її не бачать.
    /// </summary>
    public long Generation => Interlocked.Read(ref generation);

    public ImportStatus Status => Volatile.Read(ref status);

    /// <summary>Для сторінок, до яких без випуску не пускає <see cref="Web.EditionGate"/>.</summary>
    public LoadedEdition Require() =>
        Current ?? throw new InvalidOperationException("Випуск даних ще не завантажено");

    public void Publish(LoadedEdition edition, DateTimeOffset at)
    {
        Volatile.Write(ref current, edition);
        Volatile.Write(ref status, new ImportStatus(at, at, null));
        Interlocked.Increment(ref generation);
    }

    /// <summary>Знімок історії закомічено: відповіді з історією, зібрані раніше, застаріли.</summary>
    public void HistoryChanged() => Interlocked.Increment(ref generation);

    public void Checked(DateTimeOffset at) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastError = null });

    public void Failed(DateTimeOffset at, string error) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastError = error });
}

/// <param name="LastLoaded">Коли востаннє завантажено новий випуск.</param>
/// <param name="LastChecked">Коли востаннє питали джерело, разом з відповідями 304.</param>
/// <param name="LastError">Чому не вдалося останнього разу; null, якщо вдалося.</param>
public sealed record ImportStatus(DateTimeOffset? LastLoaded, DateTimeOffset? LastChecked, string? LastError);
