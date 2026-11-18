using System.Globalization;
using DreamPovnota.Digest;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Changes;

/// <summary>Підписи й речення сторінки змін і стрічки Atom: одні й ті самі слова в обох.</summary>
public static class ChangeText
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>«16.11–22.11.2026» для тижня, «середа, 18.11.2026» для дня.</summary>
    public static string Title(PeriodChanges period, PeriodKind kind) => kind == PeriodKind.Day
        ? Weekday[(int)period.From.DayOfWeek] + ", " + Format.Date(period.From)
        : DigestText.Period(kind == PeriodKind.Total ? period.ObservedFrom : period.From, period.To);

    // свої назви днів, а не з культури uk-UA: там апостроф типографський, а на решті сторінок звичайний;
    // DayOfWeek.Sunday нульовий
    private static readonly string[] Weekday = ["неділя", "понеділок", "вівторок", "середа", "четвер", "п'ятниця", "субота"];

    // знахідний відмінок для «почалась у середу»
    private static readonly string[] OnWeekday = ["у неділю", "у понеділок", "у вівторок", "у середу", "у четвер", "у п'ятницю", "у суботу"];

    /// <summary>«у середу 18.11.2026».</summary>
    public static string OnDay(DateOnly day) => OnWeekday[(int)day.DayOfWeek] + " " + Format.Date(day);

    /// <summary>«За 18.11.2026», якщо період в один день, інакше «З 18.11 по 14.12.2026».</summary>
    public static string Span(PeriodChanges period)
    {
        var (from, to) = (period.ObservedFrom, period.To);
        if (from == to) return "За " + Format.Date(to);
        string start = from.Year == to.Year ? from.ToString("dd.MM", Ukrainian) : Format.Date(from);
        return $"З {start} по {Format.Date(to)}";
    }

    /// <summary>Підпис під стовпчиком: перший день періоду.</summary>
    public static string Short(PeriodChanges period) => period.From.ToString("dd.MM", Ukrainian);

    public static string Fields(int count) => Format.Number(count) + " " + Format.Plural(count, "поле", "поля", "полів");

    /// <summary>«Сектор 10, Мета 5».</summary>
    public static string FieldList(IEnumerable<FieldCount> fields) =>
        string.Join(", ", fields.Select(f => f.Title + " " + Format.Number(f.Fields)));

    /// <summary>«Ідея → Визначено 3».</summary>
    public static string StageList(IEnumerable<StageMove> stages) =>
        string.Join(", ", stages.Select(s => $"{s.FromTitle} → {s.ToTitle} {Format.Number(s.Cards)}"));

    /// <summary>«Зміни були в 4 з 22 організацій переліку.» Без назв: служби між собою не порівнюються.</summary>
    public static string ServicesChanged(int count) =>
        $"Зміни були в {Format.Number(count)} з {Format.Number(DigestOrganisations.All.Count)} організацій переліку.";

    /// <summary>Чому чисел за період немає або чому вони нулі; null, якщо зміни є.</summary>
    public static string? Nothing(PeriodChanges period, ChangeCounts counts, PeriodKind kind)
    {
        string unit = kind == PeriodKind.Day ? "день" : "тиждень";
        if (period.ComparedFrom is null)
        {
            if (period.Snapshots == 0) return kind == PeriodKind.Day
                ? "Знімка за цей день немає; зміни, якщо були, видно в наступному дні зі знімком."
                : "Знімків за цей тиждень немає.";
            return "Тут тільки перший знімок історії, з ним порівнюються наступні.";
        }
        return counts.HasChanges ? null : $"За цей {unit} змін у відкритих даних не було.";
    }

    /// <summary>Застереження до періоду: неповний, ще триває, змінився перелік перевірок.</summary>
    public static IEnumerable<string> Notes(PeriodChanges period, PeriodKind kind)
    {
        if (kind == PeriodKind.Week && period.ObservedFrom > period.From)
            yield return $"Історія з {Format.Date(period.ObservedFrom)}.";
        if (period.InProgress && period.ComparedTo is { } to)
            yield return $"{(kind == PeriodKind.Day ? "День" : "Тиждень")} триває, дані на {Format.DateTime(to)}.";
        if (period.RulesChanged)
            yield return "Перелік перевірок змінився, тож поля порівнюються тільки ті, що є в обох переліках.";
    }

    /// <summary>Неповний період на графіку світліший: історія почалась усередині або він ще триває.</summary>
    public static bool IsPartial(PeriodChanges period) => period.InProgress || period.ObservedFrom > period.From;
}
