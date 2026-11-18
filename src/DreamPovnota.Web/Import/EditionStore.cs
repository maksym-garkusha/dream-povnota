namespace DreamPovnota.Web.Import;

/// <summary>
/// Поточний випуск у пам'яті. Новий випуск підміняє старий одним записом посилання,
/// тому запити бачать або старий, або новий, але ніколи напівготовий.
/// </summary>
public sealed class EditionStore
{
    private LoadedEdition? current;
    private ImportStatus status = new(null, null, null, null);
    private string? held;
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

    /// <summary>Чому запобіжник не прийняв останній новий випуск; null, якщо він нічого не тримає.</summary>
    public string? HeldReason => Volatile.Read(ref held);

    /// <summary>Для сторінок, до яких без випуску не пускає <see cref="Web.EditionGate"/>.</summary>
    public LoadedEdition Require() =>
        Current ?? throw new InvalidOperationException("Випуск даних ще не завантажено");

    public void Publish(LoadedEdition edition, DateTimeOffset at)
    {
        Volatile.Write(ref current, edition);
        Volatile.Write(ref held, null);
        Volatile.Write(ref status, new ImportStatus(at, at, at, null));
        Interlocked.Increment(ref generation);
    }

    /// <summary>
    /// Запобіжник не прийняв новий випуск. Причина лишається в статусі й тоді, коли Pages
    /// далі відповідають 304 на той самий файл: зникає тільки з наступним прийнятим випуском.
    /// </summary>
    public void Held(DateTimeOffset at, string reason)
    {
        Volatile.Write(ref held, reason);
        Volatile.Write(ref status, Status with { LastChecked = at, LastSucceeded = at, LastError = reason });
    }

    /// <summary>Знімок історії закомічено: відповіді з історією, зібрані раніше, застаріли.</summary>
    public void HistoryChanged() => Interlocked.Increment(ref generation);

    public void Checked(DateTimeOffset at) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastSucceeded = at, LastError = Volatile.Read(ref held) });

    public void Failed(DateTimeOffset at, string error) =>
        Volatile.Write(ref status, Status with { LastChecked = at, LastError = error });
}

/// <param name="LastLoaded">Коли востаннє завантажено новий випуск.</param>
/// <param name="LastChecked">Коли востаннє питали джерело, разом з відповідями 304.</param>
/// <param name="LastSucceeded">
/// Коли джерело востаннє віддало читабельний файл або 304, навіть якщо запобіжник файл не прийняв.
/// Збої мережі й зіпсований файл цей час не зсувають.
/// </param>
/// <param name="LastError">Чому не вдалося останнього разу або чому запобіжник не прийняв новий випуск; null, якщо все гаразд.</param>
public sealed record ImportStatus(DateTimeOffset? LastLoaded, DateTimeOffset? LastChecked, DateTimeOffset? LastSucceeded, string? LastError);
