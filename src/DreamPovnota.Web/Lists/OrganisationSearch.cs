using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Lists;

public static class OrganisationSearch
{
    /// <summary>
    /// Точний ЄДРПОУ з 8 цифр дає ту організацію; інакше пошук частини назви без регістру.
    /// Порожній запит повертає всі організації за назвою.
    /// </summary>
    public static IReadOnlyList<OrganisationEntry> Find(LoadedEdition edition, string? query)
    {
        if (string.IsNullOrEmpty(query)) return edition.OrganisationList;
        if (query.Length == 8 && query.All(char.IsAsciiDigit))
            return edition.OrganisationsByKey.TryGetValue(query, out var exact) ? [exact] : [];
        return edition.OrganisationList.Where(o => o.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Kestrel не розкодовує %2F у шляху, щоб не змінити його сегменти, тож слеш
    /// у назві організації приходить у ключ закодованим.
    /// </summary>
    public static string NormaliseKey(string key) => key.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
}
