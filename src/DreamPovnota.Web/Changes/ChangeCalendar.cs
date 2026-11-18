using System.Globalization;
using DreamPovnota.Web.History;

namespace DreamPovnota.Web.Changes;

/// <summary>Тиждень чи день за київським часом: дати включно і межі за UTC.</summary>
/// <param name="Start">Північ першого дня за Києвом.</param>
/// <param name="End">Північ після останнього дня; сам момент у період уже не входить.</param>
public sealed record PeriodSpan(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// Тижні й дні сторінки змін. Тиждень ISO, з понеділка, межі за київським часом; знімок
/// належить тому періоду, у якому його випущено. Кожен період порівнює два знімки:
/// останній до початку і останній до кінця, тож дві зміни однієї картки за тиждень
/// рахуються один раз.
/// </summary>
public static class ChangeCalendar
{
    /// <summary>Скільки останніх тижнів показувати: квартал, щоб графік лишався читабельним і на телефоні.</summary>
    public const int MaxWeeks = 12;

    // в образі без бази часових поясів межі за UTC, як і дати на інших сторінках
    private static readonly TimeZoneInfo Kyiv =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Kyiv", out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateOnly KyivDate(DateTimeOffset moment) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, Kyiv).DateTime);

    /// <summary>Північ дня за Києвом; перехід на літній час у Києві о третій ночі, тож північ завжди існує й одна.</summary>
    public static DateTimeOffset Midnight(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Kyiv), TimeSpan.Zero);

    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>«2026-W47».</summary>
    public static string IsoWeek(DateOnly monday)
    {
        var date = monday.ToDateTime(TimeOnly.MinValue);
        return ISOWeek.GetYear(date).ToString(CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(date).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Тижні від тижня першого знімка до тижня останнього, не більше <see cref="MaxWeeks"/>.</summary>
    public static IReadOnlyList<PeriodSpan> Weeks(DateTimeOffset first, DateTimeOffset last)
    {
        var start = Monday(KyivDate(first));
        var end = Monday(KyivDate(last));
        var from = end.AddDays(-7 * (MaxWeeks - 1));
        if (from < start) from = start;

        var weeks = new List<PeriodSpan>();
        for (var monday = from; monday <= end; monday = monday.AddDays(7)) weeks.Add(Span(monday, monday.AddDays(6)));
        return weeks;
    }

    /// <summary>Дні першого тижня історії: від дня першого знімка до неділі, але не далі дня останнього.</summary>
    public static IReadOnlyList<PeriodSpan> FirstWeekDays(DateTimeOffset first, DateTimeOffset last)
    {
        var day = KyivDate(first);
        var to = Monday(day).AddDays(6);
        var lastDay = KyivDate(last);
        if (lastDay < to) to = lastDay;

        var days = new List<PeriodSpan>();
        for (; day <= to; day = day.AddDays(1)) days.Add(Span(day, day));
        return days;
    }

    public static PeriodSpan Span(DateOnly from, DateOnly to) => new(from, to, Midnight(from), Midnight(to.AddDays(1)));

    /// <summary>
    /// Знімки на краях періоду: останній до початку і останній до кінця. Якщо історія
    /// почалась усередині періоду, початком береться перший знімок. Null, якщо в період
    /// не потрапив жоден новий знімок і порівнювати нема чого.
    /// </summary>
    /// <param name="snapshots">Усі знімки за зростанням номера.</param>
    public static (SnapshotMark From, SnapshotMark To)? Edges(IReadOnlyList<SnapshotMark> snapshots, PeriodSpan span)
    {
        SnapshotMark? from = null, to = null;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Generated < span.Start) from = snapshot;
            if (snapshot.Generated < span.End) to = snapshot;
        }
        from ??= snapshots.Count > 0 ? snapshots[0] : null;
        return from is not null && to is not null && to.Id > from.Id ? (from, to) : null;
    }

    public static int SnapshotsIn(IReadOnlyList<SnapshotMark> snapshots, PeriodSpan span) =>
        snapshots.Count(s => s.Generated >= span.Start && s.Generated < span.End);
}
