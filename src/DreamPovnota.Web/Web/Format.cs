using System.Globalization;

namespace DreamPovnota.Web.Web;

/// <summary>Числа й дати для сторінок: українські роздільники, час за Києвом.</summary>
public static class Format
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    // в образі без бази часових поясів показуємо UTC, а не падаємо
    private static readonly TimeZoneInfo Kyiv =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Kyiv", out var zone) ? zone : TimeZoneInfo.Utc;

    public static string Number(int value) => value.ToString("N0", Ukrainian);

    /// <summary>Частка від 0 до 1 як «92,6 %».</summary>
    public static string Percent(double share) => share.ToString("P1", Ukrainian);

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Ukrainian);

    public static string Date(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, Kyiv).ToString("dd.MM.yyyy", Ukrainian);

    public static string DateTime(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, Kyiv).ToString("dd.MM.yyyy, HH:mm", Ukrainian)
        + (Kyiv == TimeZoneInfo.Utc ? " UTC" : " за Києвом");

    /// <summary>«1 картка», «3 картки», «12 карток».</summary>
    public static string Cards(int count) => Number(count) + " " + Plural(count, "картка", "картки", "карток");

    /// <summary>«у 1 картці», «у 12 картках».</summary>
    public static string InCards(int count) => "у " + Number(count) + " " + Plural(count, "картці", "картках", "картках");

    public static string Plural(int count, string one, string few, string many)
    {
        int lastTwo = Math.Abs(count) % 100;
        int last = lastTwo % 10;
        if (lastTwo is >= 11 and <= 14) return many;
        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    public static string OrganisationPath(string key) => "/org/" + Uri.EscapeDataString(key);

    public static string RegionPath(string key) => "/region/" + key;

    public static string PortalUrl(string code) => Import.LoadedEdition.PortalPrefix + code + "/profile";
}
