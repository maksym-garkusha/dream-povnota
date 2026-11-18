using System.Text.Json;
using System.Text.RegularExpressions;

namespace DreamPovnota.Cards;

/// <summary>
/// Розбір відповідей /ideas/{id} і /objects/{id}. Що в яких полях лежить,
/// описано в README; тут тільки те, що з опису не видно.
/// </summary>
public static partial class CardParser
{
    public const int MinDescriptionLength = 30;

    /// <exception cref="JsonException">Відповідь не JSON або в ній немає id, коду чи дати зміни.</exception>
    public static ProjectCard ParseProject(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var card = root.Prop("cdu_response")
            ?? throw new JsonException("У відповіді немає cdu_response");

        string id = Required(root.Prop("internal").Prop("id").Text(), "internal.id");
        var approaches = card.Prop("approaches").Items().ToList();

        // кошторис і джерела фінансування лежать у approaches[].budget; верхній
        // cdu_response.budget у даних містить тільки рядки "deficient" без сум
        var budgets = approaches.Select(a => a.Prop("budget")).ToList();
        var breakdown = budgets.SelectMany(b => b.Prop("budgetBreakdown").Items())
            .Where(row => row.Prop("classifications").Prop("status").Text() != "cancelled");
        var finance = budgets.SelectMany(b => b.Prop("finance").Items())
            .Where(source => source.Prop("status").Text() != "cancelled")
            .ToList();
        var processes = card.Prop("contractingProcesses").Items()
            .Where(p => p.Prop("summary").Prop("status").Text() != "cancelled")
            .ToList();
        var contracts = processes.SelectMany(p => p.Prop("summary").Prop("contracts").Items()).ToList();

        var description = Translation(card.Prop("description"), "uk");

        return new ProjectCard
        {
            Id = id,
            Code = card.Prop("code").Text() ?? "DREAM-UA-" + Required(root.Prop("internal").Prop("code").Text(), "code"),
            Status = root.Prop("internal").Prop("status").Text() ?? "",
            Stage = Stages.FromStatus(root.Prop("internal").Prop("status").Text()),
            Type = card.Prop("type").Text(),
            Purpose = card.Prop("purpose").Text(),
            Created = card.Prop("date").Date(),
            Updated = card.Prop("updated").Date() ?? throw new JsonException($"У картці {id} немає updated"),
            Title = CardText.OneLine(Translation(card.Prop("title"), "uk")),
            HasEnglishTitle = !string.IsNullOrWhiteSpace(Translation(card.Prop("title"), "en")),
            DescriptionState = CardText.Classify(description, MinDescriptionLength),
            DescriptionLength = CardText.Clean(description).Length,
            HasEnglishDescription = CardText.Classify(Translation(card.Prop("description"), "en")) == TextState.Filled,
            GoalState = CardText.Classify(Translation(card.Prop("purposeDetails"), "uk")),
            DurationMonths = card.Prop("period").Prop("durationInMonth").Number(),
            Parties = card.Prop("parties").Items().Select(ParseParty).ToList(),
            SectorCode = card.Prop("additionalClassifications").Items()
                .FirstOrDefault(c => c.Prop("scheme").Text() == "WB-ECO") is { ValueKind: JsonValueKind.Object } eco
                ? eco.Prop("id").Text() ?? ""
                : null,
            ObjectIds = card.Prop("relatedProcesses").Items()
                .Where(r => r.Prop("scheme").Text() == "UA-DREAM" && r.Prop("relationship").Text() == "object")
                .Select(r => r.Prop("identifier").Text())
                .OfType<string>()
                .Distinct()
                .ToList(),
            Katottg = Katottg(approaches
                .SelectMany(a => a.Prop("items").Items())
                .SelectMany(i => i.Prop("locations").Items())),
            DocumentCount = card.Prop("documents").Count() + approaches.Sum(a => a.Prop("documents").Count()),
            CostEstimate = breakdown.Sum(row => row.Prop("amount").Prop("amount").Number() ?? 0),
            FundingSourceCount = finance.Count,
            FundingTotal = finance.Sum(Amount),
            FundingCommitted = finance.Where(s => s.Prop("status").Text() == "committed").Sum(Amount),
            FundingIndicative = finance.Where(s => s.Prop("status").Text() == "indicative").Sum(Amount),
            FinanceCategories = finance.Select(s => s.Prop("financeCategory").Text()).OfType<string>().Distinct().ToList(),
            ContractingProcesses = processes.Count,
            HasProzorroLink = processes.SelectMany(p => p.Prop("relatedProcesses").Items())
                .Any(r => r.Prop("scheme").Text() == "UA-PROZORRO"),
            ContractCount = contracts.Count,
            ContractsValue = contracts.Sum(c => c.Prop("value").Prop("amount").Number() ?? 0),
            HasCompletion = card.Prop("completion") is { ValueKind: JsonValueKind.Object },
        };
    }

    /// <exception cref="JsonException">Відповідь не JSON або в ній немає id чи дати зміни.</exception>
    public static ObjectCard ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var card = root.Prop("cdu_response")
            ?? throw new JsonException("У відповіді немає cdu_response");

        string id = Required(root.Prop("internal").Prop("id").Text(), "internal.id");

        return new ObjectCard(
            id,
            card.Prop("updated").Date() ?? throw new JsonException($"В об'єкті {id} немає updated"),
            Katottg(card.Prop("locations").Items()),
            card.Prop("documents").Count());
    }

    /// <summary>
    /// Персональні дані сюди не потрапляють: <c>persons</c> не читаємо зовсім,
    /// а в підприємця не беремо ні код (РНОКПП), ні назву (ім'я).
    /// </summary>
    private static Party ParseParty(JsonElement party)
    {
        var identifier = party.Prop("identifier");
        // схему й код вводять руками: «ua-ipn», «UA-IPN », «123 456 7899» і «123-456-7899» це те саме
        string? scheme = identifier.Prop("scheme").Text()?.Trim().ToUpperInvariant();
        string code = string.Concat((identifier.Prop("id").Text() ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-'));
        string? name = CleanName(identifier.Prop("legalName").Text());
        var roles = party.Prop("roles").Items().Select(r => r.Text()).OfType<string>().ToList();

        bool digits = code.Length > 0 && code.All(char.IsAsciiDigit);
        var soleProprietor = new Party(PartyKind.SoleProprietor, scheme, null, null, roles);

        return scheme switch
        {
            "UA-IPN" => soleProprietor,
            "UA-EDR" when digits && code.Length == 8 => new Party(PartyKind.Organisation, scheme, code, name, roles),
            // десять цифр під схемою UA-EDR це РНОКПП підприємця
            "UA-EDR" when digits && code.Length == 10 => soleProprietor,
            // за назвою «ФОП …» стоїть ім'я людини, хоч би яку схему вказали
            _ when name is not null && NamesPerson(name) => soleProprietor,
            "UA-EDR" => new Party(PartyKind.OddCode, scheme, null, name, roles),
            _ => new Party(PartyKind.OtherRegistry, scheme, null, name, roles),
        };
    }

    private static bool NamesPerson(string name) =>
        SoleProprietorPattern().IsMatch(name) || name.Contains("фізична особа", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<!\p{L})ФОП(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoleProprietorPattern();

    private static List<string> Katottg(IEnumerable<JsonElement> locations) =>
        locations
            .Select(l => l.Prop("gazetteer"))
            .Where(g => g.Prop("scheme").Text() == "UA-CATOTTG")
            .SelectMany(g => g.Prop("identifiers").Items())
            .Select(code => code.Text()?.Trim())
            .Where(code => !string.IsNullOrEmpty(code))
            .OfType<string>()
            .Distinct()
            .ToList();

    // мультимовні поля: [{lang:"uk", translation:"..."}, {lang:"en", ...}]
    private static string? Translation(JsonElement? values, string lang) =>
        values.Items().FirstOrDefault(v => v.Prop("lang").Text() == lang) is { ValueKind: JsonValueKind.Object } value
            ? value.Prop("translation").Text()
            : null;

    private static decimal Amount(JsonElement source) => source.Prop("value").Prop("amount").Number() ?? 0;

    private static string? CleanName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : CardText.OneLine(name);

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new JsonException($"У відповіді немає {field}") : value;
}
