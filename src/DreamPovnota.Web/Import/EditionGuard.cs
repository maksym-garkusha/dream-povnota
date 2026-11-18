using DreamPovnota.Export;
using DreamPovnota.Web.History;
using Microsoft.Extensions.Options;

namespace DreamPovnota.Web.Import;

/// <summary>Пороги запобіжника, розділ ImportGuard у налаштуваннях. Звідки ці числа, сказано в README.</summary>
public sealed class GuardLimits
{
    public const string Section = "ImportGuard";

    /// <summary>На скільки відсотків може змінитися кількість відкритих карток між двома випусками.</summary>
    public double OpenCardsPercent { get; set; } = 5;

    /// <summary>Те саме для кількості організацій, що ведуть відкриті картки.</summary>
    public double OrganisationsPercent { get; set; } = 5;

    /// <summary>Яка частка карток може змінитися за раз: нові, зниклі і з іншими доречними чи заповненими полями.</summary>
    public double ChangedCardsPercent { get; set; } = 20;

    /// <summary>
    /// SHA-256 випуску, який треба прийняти попри стрибок, коли зміна справжня: наприклад,
    /// DREAM масово оновив картки або змінилось правило перевірки. Діє лише на цей один файл.
    /// </summary>
    public string? AcceptSha256 { get; set; }
}

public readonly record struct CardMasks(int Applicable, int Filled);

/// <summary>Стан, з яким порівнюється новий випуск: поточний у пам'яті або останній знімок у базі.</summary>
/// <param name="Organisations">Скільки різних організацій ведуть відкриті картки.</param>
/// <param name="Checks">Коди перевірок у порядку бітів масок.</param>
public sealed record GuardBaseline(int Organisations, IReadOnlyList<string> Checks, IReadOnlyDictionary<string, CardMasks> Cards)
{
    public int Open => Cards.Count;

    public static GuardBaseline Of(Edition edition) => new(
        OrganisationCount(edition),
        edition.Checks.Select(c => c.Code).ToList(),
        edition.Cards.ToDictionary(c => c.Code, c => new CardMasks(c.Applicable, c.Filled), StringComparer.Ordinal));

    public static int OrganisationCount(Edition edition) =>
        edition.Cards.Select(c => edition.Organisations[c.Organisation].Key).Distinct(StringComparer.Ordinal).Count();
}

public enum JumpKind
{
    OpenCards,
    Organisations,
    ChangedCards,
}

/// <summary>Стрибок, через який випуск не прийнято.</summary>
/// <param name="Before">Було: відкритих карток, організацій або, для змінених карток, усього карток.</param>
/// <param name="After">Стало; для змінених карток скільки їх змінилось.</param>
public sealed record Jump(JumpKind Kind, int Before, int After, double Percent, double Limit)
{
    /// <summary>Для /health/ready: тільки тип, без чисел, як і для інших збоїв імпорту.</summary>
    public string Reason => "EditionGuard: " + Kind switch
    {
        JumpKind.OpenCards => "open-cards",
        JumpKind.Organisations => "organisations",
        _ => "changed-cards",
    };

    public override string ToString() => Kind switch
    {
        JumpKind.OpenCards => $"відкритих карток було {Before}, стало {After} ({Percent:0.0} % при порозі {Limit} %)",
        JumpKind.Organisations => $"організацій було {Before}, стало {After} ({Percent:0.0} % при порозі {Limit} %)",
        _ => $"змінених карток {After} з {Before} ({Percent:0.0} % при порозі {Limit} %)",
    };
}

/// <summary>
/// Запобіжник від підміненого чи зіпсованого data.json: випуск, у якому за раз змінилось
/// набагато більше, ніж буває за день, не стає поточним. Порівняння йде з поточним випуском,
/// а після перезапуску, коли в пам'яті ще нічого немає, з останнім знімком у базі.
/// </summary>
public sealed class EditionGuard(HistoryReader history, IOptions<GuardLimits> limits, ILogger<EditionGuard> log)
{
    /// <returns>Стрибок, через який випуск не можна приймати, або null.</returns>
    public async Task<Jump?> CheckAsync(LoadedEdition incoming, LoadedEdition? current, CancellationToken ct)
    {
        var options = limits.Value;
        if (options.AcceptSha256 is { Length: > 0 } accepted
            && string.Equals(accepted.Trim(), incoming.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("Випуск {Sha256} прийнято без порівняння: його хеш задано в ImportGuard:AcceptSha256", incoming.Sha256);
            return null;
        }

        var baseline = current is not null ? GuardBaseline.Of(current.Edition) : await history.GuardBaselineAsync(ct);
        if (baseline is null)
        {
            // перший запуск або база не відповіла: не пускати випуск означало б 503 на всіх сторінках
            if (history.IsConfigured)
                log.LogWarning("Попереднього випуску немає ні в пам'яті, ні в базі: випуск {Sha256} прийнято без порівняння", incoming.Sha256);
            return null;
        }
        return Compare(baseline, incoming.Edition, options);
    }

    public static Jump? Compare(GuardBaseline before, Edition after, GuardLimits limits)
    {
        double open = Relative(before.Open, after.Cards.Count);
        if (open > limits.OpenCardsPercent)
            return new Jump(JumpKind.OpenCards, before.Open, after.Cards.Count, open, limits.OpenCardsPercent);

        int organisations = GuardBaseline.OrganisationCount(after);
        double organisationShift = Relative(before.Organisations, organisations);
        if (organisationShift > limits.OrganisationsPercent)
            return new Jump(JumpKind.Organisations, before.Organisations, organisations, organisationShift, limits.OrganisationsPercent);

        int changed = ChangedCards(before, after);
        double share = before.Open == 0 ? 0 : 100.0 * changed / before.Open;
        return share > limits.ChangedCardsPercent
            ? new Jump(JumpKind.ChangedCards, before.Open, changed, share, limits.ChangedCardsPercent)
            : null;
    }

    /// <summary>
    /// Нові картки, зниклі і ті, у яких інакше доречне чи заповнене. Без нових і зниклих
    /// підміну всіх кодів при тій самій кількості карток не було б видно.
    /// </summary>
    private static int ChangedCards(GuardBaseline before, Edition after)
    {
        // біти порівнюються за кодами перевірок: перелік і порядок між випусками могли змінитися,
        // а нова перевірка сама по собі картки зміненими не робить
        var bitBefore = before.Checks.Select((code, i) => (code, i)).ToDictionary(x => x.code, x => x.i, StringComparer.Ordinal);
        var common = after.Checks
            .Select((check, bit) => (bit, old: bitBefore.GetValueOrDefault(check.Code, -1)))
            .Where(x => x.old >= 0)
            .ToList();

        int changed = 0, kept = 0;
        foreach (var card in after.Cards)
        {
            if (!before.Cards.TryGetValue(card.Code, out var old))
            {
                changed++;
                continue;
            }
            kept++;
            if (common.Any(x => Bit(card.Applicable, x.bit) != Bit(old.Applicable, x.old) || Bit(card.Filled, x.bit) != Bit(old.Filled, x.old)))
                changed++;
        }
        return changed + (before.Open - kept);
    }

    private static bool Bit(int mask, int bit) => (mask & (1 << bit)) != 0;

    private static double Relative(int before, int after) => before == 0 ? 0 : 100.0 * Math.Abs(after - before) / before;
}
