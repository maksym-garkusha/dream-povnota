using System.Text.Json.Serialization;

namespace DreamPovnota.Cards;

/// <summary>
/// Те, що лишається від картки проєкту після розбору. Саме це лежить у кеші,
/// тому тут немає ні сирого JSON, ні імен людей.
/// </summary>
public record ProjectCard
{
    public required string Id { get; init; }

    /// <summary>Код на порталі, "DREAM-UA-190424-A10FC799".</summary>
    public required string Code { get; init; }

    /// <summary>Сирий <c>internal.status</c>, наприклад "cancelled:duplicate".</summary>
    public required string Status { get; init; }

    public required Stage Stage { get; init; }

    /// <summary>rehabilitation, construction, equipment, replacement, expansion.</summary>
    public string? Type { get; init; }

    /// <summary>restoration або development. Мета проєкту словами це <see cref="GoalState"/>.</summary>
    public string? Purpose { get; init; }

    public DateTimeOffset? Created { get; init; }

    public required DateTimeOffset Updated { get; init; }

    /// <summary>Назва українською, пробіли згорнуто; порожній рядок, якщо назви немає.</summary>
    public string Title { get; init; } = "";

    public bool HasEnglishTitle { get; init; }

    public TextState DescriptionState { get; init; }

    /// <summary>Довжина опису українською після зняття тегів.</summary>
    public int DescriptionLength { get; init; }

    /// <summary>Опис англійською є і це не заглушка (порога довжини немає).</summary>
    public bool HasEnglishDescription { get; init; }

    /// <summary>Стан <c>purposeDetails</c> українською (мета проєкту). Порога довжини немає.</summary>
    public TextState GoalState { get; init; }

    /// <summary><c>period.durationInMonth</c> як є; на ранніх стадіях його немає.</summary>
    public decimal? DurationMonths { get; init; }

    public IReadOnlyList<Party> Parties { get; init; } = [];

    /// <summary>Перший код WB-ECO як є ("ES"); null, якщо такої класифікації немає.</summary>
    public string? SectorCode { get; init; }

    /// <summary>Ідентифікатори для /objects/{id}.</summary>
    public IReadOnlyList<string> ObjectIds { get; init; } = [];

    /// <summary>Коди КАТОТТГ з <c>approaches</c>, без повторів. Коди об'єктів окремо, в <see cref="ObjectCard"/>.</summary>
    public IReadOnlyList<string> Katottg { get; init; } = [];

    /// <summary>Документи самого проєкту і його <c>approaches</c>. Документи об'єктів окремо.</summary>
    public int DocumentCount { get; init; }

    /// <summary>Сума нескасованих рядків <c>budgetBreakdown</c> у всіх <c>approaches</c>.</summary>
    public decimal CostEstimate { get; init; }

    /// <summary>Кількість нескасованих джерел фінансування, зокрема з нульовою сумою.</summary>
    public int FundingSourceCount { get; init; }

    /// <summary>Сума всіх джерел фінансування, крім <c>cancelled</c>.</summary>
    public decimal FundingTotal { get; init; }

    /// <summary>Сума джерел зі статусом <c>committed</c> (підтверджене фінансування).</summary>
    public decimal FundingCommitted { get; init; }

    /// <summary>Сума джерел зі статусом <c>indicative</c>.</summary>
    public decimal FundingIndicative { get; init; }

    /// <summary>financeCategory нескасованих джерел без повторів: localBudget, centralBudget...</summary>
    public IReadOnlyList<string> FinanceCategories { get; init; } = [];

    /// <summary>Контрактні процеси, у яких <c>summary.status</c> не <c>cancelled</c>.</summary>
    public int ContractingProcesses { get; init; }

    /// <summary>Хоч один нескасований контрактний процес посилається на Prozorro.</summary>
    public bool HasProzorroLink { get; init; }

    /// <summary>Договори в нескасованих контрактних процесах.</summary>
    public int ContractCount { get; init; }

    /// <summary>
    /// Сума договорів, у яких вказано суму. У договорів з Prozorro суми в DREAM
    /// зазвичай немає, тож це нижня межа, а не повна сума.
    /// </summary>
    public decimal ContractsValue { get; init; }

    /// <summary>Є розділ <c>completion</c> (буває у завершених).</summary>
    public bool HasCompletion { get; init; }

    [JsonIgnore]
    public bool IsOpen => Stage.IsOpen();

    [JsonIgnore]
    public char? SectorLetter => string.IsNullOrEmpty(SectorCode) ? null : char.ToUpperInvariant(SectorCode[0]);

    /// <summary>
    /// Хто веде картку: сторона з роллю projectAuthor, інакше перший initiator,
    /// інакше перша сторона.
    /// </summary>
    [JsonIgnore]
    public Party? Organisation =>
        Parties.FirstOrDefault(p => p.HasRole("projectAuthor"))
        ?? Parties.FirstOrDefault(p => p.HasRole("initiator"))
        ?? Parties.FirstOrDefault();
}
