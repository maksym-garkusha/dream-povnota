using System.Globalization;
using System.Text;
using System.Xml;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Feeds;

/// <summary>
/// Atom-стрічка змін організації чи області: один запис на знімок, у якому щось
/// змінилось. Текст описує поля, а не людей чи установи.
/// </summary>
public static class AtomFeed
{
    public const string ContentType = "application/atom+xml; charset=utf-8";

    public static void MapFeeds(this IEndpointRouteBuilder app)
    {
        var feeds = app.MapGroup("/feeds").ExcludeFromDescription().CacheOutput(ImportService.CacheTag);

        feeds.MapGet("/org/{key}.atom", (string key, HttpContext context, EditionStore store, HistoryReader history, CancellationToken ct) =>
        {
            key = OrganisationSearch.NormaliseKey(key);
            var edition = store.Require();
            return edition.OrganisationsByKey.TryGetValue(key, out var organisation)
                ? WriteAsync(context, edition, history, "org", key, organisation.Title, new HistoryScope(Organisation: key), Format.OrganisationPath(key), ct)
                : Task.FromResult(Results.NotFound());
        });
        feeds.MapGet("/region/{key}.atom", (string key, HttpContext context, EditionStore store, HistoryReader history, CancellationToken ct) =>
        {
            var edition = store.Require();
            return edition.RegionIndex.TryGetValue(key, out int index)
                ? WriteAsync(context, edition, history, "region", key, edition.Edition.Regions[index].Title, new HistoryScope(Region: key), Format.RegionPath(key), ct)
                : Task.FromResult(Results.NotFound());
        });
    }

    private static async Task<IResult> WriteAsync(
        HttpContext context, LoadedEdition edition, HistoryReader history, string scope, string key, string title,
        HistoryScope historyScope, string pagePath, CancellationToken ct)
    {
        var snapshots = await history.SnapshotsAsync(HistoryReader.FeedSnapshots, ct);
        var changes = snapshots is null ? null : await history.ChangesAsync(historyScope, HistoryReader.FeedSnapshots, ct);
        if (snapshots is null || changes is null)
        {
            context.Response.Headers.RetryAfter = "300";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var request = context.Request;
        string site = $"{request.Scheme}://{request.Host}";
        string xml = Build(site, request.Host.Host, scope, key, title, pagePath, snapshots, changes, Titles(edition));
        return Results.Text(xml, ContentType, Encoding.UTF8);
    }

    public static string Build(
        string site, string host, string scope, string key, string title, string pagePath,
        IReadOnlyList<SnapshotInfo> snapshots, IReadOnlyList<CardChange> changes, IReadOnlyDictionary<string, string> titles)
    {
        var byShot = changes.GroupBy(c => c.Snapshot).ToDictionary(g => g.Key, g => g.ToList());
        // перший знімок нічого не порівнює, тому записів для нього немає
        int first = snapshots.Count == 0 ? 0 : snapshots.Min(s => s.Id);
        var entries = snapshots
            .Where(s => s.Id != first && (s.RulesChanged || byShot.ContainsKey(s.Id)))
            .ToList();

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), Async = false };
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        using (var xml = XmlWriter.Create(buffer, settings))
        {
            const string atom = "http://www.w3.org/2005/Atom";
            xml.WriteStartElement("feed", atom);
            xml.WriteElementString("id", atom, $"tag:{host},2026:{scope}/{key}");
            xml.WriteElementString("title", atom, $"Зміни карток DREAM: {title}");
            xml.WriteElementString("updated", atom, Stamp(entries.FirstOrDefault()?.Generated ?? snapshots.FirstOrDefault()?.Generated ?? DateTimeOffset.UnixEpoch));
            Link(xml, atom, site + pagePath, "alternate");
            Link(xml, atom, $"{site}/feeds/{scope}/{Uri.EscapeDataString(key)}.atom", "self");
            xml.WriteStartElement("author", atom);
            xml.WriteElementString("name", atom, "dream-povnota");
            xml.WriteEndElement();

            foreach (var snapshot in entries)
            {
                xml.WriteStartElement("entry", atom);
                xml.WriteElementString("id", atom, $"tag:{host},2026:{scope}/{key}/snapshot/{snapshot.Id}");
                xml.WriteElementString("updated", atom, Stamp(snapshot.Generated));
                Link(xml, atom, site + pagePath, "alternate");
                if (snapshot.RulesChanged)
                {
                    xml.WriteElementString("title", atom, $"Знімок від {Format.Date(snapshot.Generated)}");
                    xml.WriteElementString("summary", atom, "Змінились правила перевірок, порівняння не показуємо.");
                }
                else
                {
                    var list = byShot[snapshot.Id];
                    xml.WriteElementString("title", atom, $"Знімок від {Format.Date(snapshot.Generated)}: зміни {Format.InCards(list.Count)}");
                    xml.WriteStartElement("content", atom);
                    xml.WriteAttributeString("type", "html");
                    xml.WriteString(Html(list, titles));
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return buffer.ToString();
    }

    private static void Link(XmlWriter xml, string atom, string href, string rel)
    {
        xml.WriteStartElement("link", atom);
        xml.WriteAttributeString("rel", rel);
        xml.WriteAttributeString("href", href);
        xml.WriteEndElement();
    }

    // вміст type="html" XmlWriter екранує сам, тож тут тільки екрануємо дані для HTML
    private static string Html(IEnumerable<CardChange> changes, IReadOnlyDictionary<string, string> titles)
    {
        var html = new StringBuilder("<ul>");
        foreach (var change in changes)
        {
            html.Append("<li>").Append(System.Net.WebUtility.HtmlEncode(change.Title))
                .Append(" (DREAM-UA-").Append(change.Code).Append("): ")
                .Append(System.Net.WebUtility.HtmlEncode(Describe(change, titles))).Append("</li>");
        }
        return html.Append("</ul>").ToString();
    }

    /// <summary>Назви перевірок і стадій за кодами, щоб писати словами, а не ключами.</summary>
    public static IReadOnlyDictionary<string, string> Titles(LoadedEdition edition) =>
        edition.Edition.Checks.Select(c => KeyValuePair.Create(c.Code, c.Title))
            .Concat(edition.Edition.Stages.Select(s => KeyValuePair.Create(s.Key, s.Title)))
            .ToDictionary(StringComparer.Ordinal);

    public static string Describe(CardChange change, IReadOnlyDictionary<string, string> titles) => change.Kind switch
    {
        ChangeKind.Added => "з'явилась серед відкритих карток",
        ChangeKind.Removed => "більше не серед відкритих (закрита або вилучена з DREAM)",
        _ => string.Join("; ", Parts(change, titles).DefaultIfEmpty("змінились організація, область, сектор або дата оновлення")),
    };

    private static IEnumerable<string> Parts(CardChange change, IReadOnlyDictionary<string, string> titles)
    {
        string Title(string? code) => code is null ? "" : titles.GetValueOrDefault(code, code);
        string List(IEnumerable<string> codes) => string.Join(", ", codes.Select(Title));

        if (change.StageFrom != change.StageTo) yield return $"стадія «{Title(change.StageFrom)}» стала «{Title(change.StageTo)}»";
        if (change.NowFilled.Count > 0) yield return "заповнено: " + List(change.NowFilled);
        if (change.NoLongerFilled.Count > 0) yield return "більше не заповнено: " + List(change.NoLongerFilled);
        if (change.NewlyApplicable.Count > 0) yield return "стали доречними: " + List(change.NewlyApplicable);
    }

    private static string Stamp(DateTimeOffset moment) => moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
