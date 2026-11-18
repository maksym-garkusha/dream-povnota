namespace DreamPovnota.Digest;

/// <summary>Картка в історії на початок тижня.</summary>
/// <param name="Edrpou">ЄДРПОУ організації, що вела картку тоді; null, якщо коду не було.</param>
/// <param name="Stage">Ключ стадії, як у data.json: "idea", "impl"...</param>
/// <param name="Applicable">Біти в порядку <see cref="DigestBaseline.Checks"/>, а не поточного випуску.</param>
public sealed record BaselineCard(string Code, string Title, string? Edrpou, string Stage, DateOnly Updated, int Applicable, int Filled);

/// <summary>
/// Стан карток організацій з переліку на початок тижня: останній знімок історії
/// не пізніше початку тижня, а якщо історія молодша, перший знімок.
/// </summary>
/// <param name="HistoryFrom">Дата випуску першого знімка: з неї ведеться історія.</param>
/// <param name="Taken">Дата випуску знімка, з яким порівнюємо.</param>
/// <param name="Checks">Коди перевірок цього знімка в порядку бітів: перелік міг змінитися.</param>
public sealed record DigestBaseline(
    DateTimeOffset HistoryFrom,
    DateTimeOffset Taken,
    IReadOnlyList<string> Checks,
    IReadOnlyList<BaselineCard> Cards);

/// <summary>Відкрита картка організації в зрізі.</summary>
/// <param name="Missing">Назви доречних, але не заповнених полів, разом з англійськими.</param>
/// <param name="FirstToAdd">Що дописати першим; null, якщо все доречне заповнено.</param>
public sealed record DigestCard(
    string Code,
    string Title,
    string Region,
    string Stage,
    string Sector,
    DateOnly Updated,
    int Filled,
    int Applicable,
    int Percent,
    IReadOnlyList<string> Missing,
    string? FirstToAdd)
{
    public string Link => DigestBuilder.PortalLink(Code);
}

public enum WeekChangeKind
{
    /// <summary>Картка була й лишилась у організації, і в ній щось змінилось.</summary>
    Updated,

    /// <summary>Картки на початок тижня в організації не було: нова або перейшла від іншої.</summary>
    Appeared,

    /// <summary>Картка була на початок тижня, а тепер серед відкритих карток організації її немає.</summary>
    Left,
}

/// <param name="Updated">Дата оновлення в DREAM тепер; для картки, якої вже немає, дата з історії.</param>
/// <param name="NowFilled">Назви полів, заповнених тепер, а на початок тижня ні.</param>
/// <param name="NoLongerFilled">Назви полів, заповнених на початок тижня, а тепер ні.</param>
public sealed record WeekChange(
    string Code,
    string Title,
    WeekChangeKind Kind,
    DateOnly Updated,
    string? StageFrom,
    string? StageTo,
    IReadOnlyList<string> NowFilled,
    IReadOnlyList<string> NoLongerFilled)
{
    public string Link => DigestBuilder.PortalLink(Code);

    public string KindTitle => Kind switch
    {
        WeekChangeKind.Updated => "Оновлено",
        WeekChangeKind.Appeared => "З'явилась серед відкритих карток організації",
        _ => "Більше не серед відкритих карток організації",
    };
}

/// <summary>Поле, яке можна дописати в кількох картках.</summary>
/// <param name="Hint">Що для цього потрібно: «вибір зі списку», «одне число».</param>
public sealed record EasyItem(string Check, string Title, string Hint, int Cards);

public enum HistoryCoverage
{
    /// <summary>Історії немає: база не підключена або не відповіла.</summary>
    Unavailable,

    /// <summary>Історія почалась уже всередині тижня.</summary>
    Partial,

    FullWeek,
}

/// <summary>Зріз однієї організації на дату випуску data.json.</summary>
/// <param name="WeekFrom">Перший день тижня; тиждень це сім днів до дати випуску включно.</param>
/// <param name="ComparedWith">Дата знімка, з яким порівнювали; null без історії.</param>
public sealed record OrganisationDigest(
    DigestOrganisation Organisation,
    DateTimeOffset Generated,
    DateOnly WeekFrom,
    DateOnly WeekTo,
    IReadOnlyList<DigestCard> Cards,
    int Complete,
    int UpdatedThisWeek,
    IReadOnlyList<EasyItem> Easiest,
    IReadOnlyList<WeekChange> Week,
    HistoryCoverage History,
    DateTimeOffset? HistoryFrom,
    DateTimeOffset? ComparedWith)
{
    public int Open => Cards.Count;

    /// <summary>Скільки полів дозаповнено за тиждень у всіх картках разом.</summary>
    public int FieldsFilledThisWeek => Week.Sum(c => c.NowFilled.Count);

    public string OrganisationPage => DigestBuilder.ServiceUrl + "/org/" + Organisation.Edrpou;

    public string DigestPage => DigestBuilder.ServiceUrl + "/digest/" + Organisation.Edrpou;

    public string WeekTitle => DigestText.Period(WeekFrom, WeekTo);

    /// <summary>Одне речення про те, наскільки повна історія за тиждень.</summary>
    public string HistoryNote => History switch
    {
        HistoryCoverage.Unavailable =>
            "Історія змін зараз недоступна, тому дозаповнені поля не показано. Оновлені картки визначено за датою оновлення в DREAM.",
        HistoryCoverage.Partial =>
            $"Історія ведеться з {DigestText.Date(HistoryFrom!.Value)}, тож дозаповнені поля видно лише від цієї дати.",
        _ => $"Порівняння зі станом карток на {DigestText.Date(ComparedWith!.Value)}.",
    };

    public string FileName(string extension) =>
        $"zriz-{Organisation.Edrpou}-{Generated.UtcDateTime:yyyy-MM-dd}.{extension}";
}
