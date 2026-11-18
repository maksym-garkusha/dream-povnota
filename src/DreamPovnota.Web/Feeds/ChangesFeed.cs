using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using DreamPovnota.Web.Changes;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Feeds;

/// <summary>
/// Atom-стрічка сторінки /changes: один запис на тиждень, найновіший перший. Запис
/// поточного тижня має той самий id і оновлюється з кожним знімком, поки тиждень триває.
/// </summary>
public static class ChangesFeed
{
    public const string Path = "/changes.atom";

    public static void MapChangesFeed(this IEndpointRouteBuilder app) =>
        app.MapGet(Path, async (HttpContext context, EditionStore store, HistoryReader history, TimeProvider clock, CancellationToken ct) =>
            {
                var summary = await ChangeSummaryBuilder.LoadAsync(history, ChangeSummaryBuilder.StagesOf(store.Require()), clock.GetUtcNow(), ct);
                if (summary is null)
                {
                    context.Response.Headers.RetryAfter = "300";
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                var request = context.Request;
                return Results.Text(Build($"{request.Scheme}://{request.Host}", request.Host.Host, summary), AtomFeed.ContentType, Encoding.UTF8);
            })
            .ExcludeFromDescription()
            .CacheOutput(ImportService.CacheTag);

    public static string Build(string site, string host, ChangeSummary summary)
    {
        const string atom = "http://www.w3.org/2005/Atom";
        return AtomFeed.Write(xml =>
        {
            xml.WriteStartElement("feed", atom);
            xml.WriteElementString("id", atom, $"tag:{host},2026:changes");
            xml.WriteElementString("title", atom, "Рух у картках DREAM за тижнями");
            xml.WriteElementString("updated", atom, AtomFeed.Stamp(summary.LastSnapshot ?? DateTimeOffset.UnixEpoch));
            AtomFeed.Link(xml, atom, site + "/changes", "alternate");
            AtomFeed.Link(xml, atom, site + Path, "self");
            xml.WriteStartElement("author", atom);
            xml.WriteElementString("name", atom, "dream-povnota");
            xml.WriteEndElement();

            foreach (var week in summary.Weeks.Reverse())
            {
                xml.WriteStartElement("entry", atom);
                xml.WriteElementString("id", atom, $"tag:{host},2026:changes/{week.IsoWeek}");
                xml.WriteElementString("title", atom, "Тиждень " + ChangeText.Title(week, PeriodKind.Week) + (week.InProgress ? ", триває" : ""));
                xml.WriteElementString("updated", atom, AtomFeed.Stamp(week.ComparedTo ?? summary.HistoryFrom ?? DateTimeOffset.UnixEpoch));
                AtomFeed.Link(xml, atom, site + "/changes", "alternate");
                xml.WriteStartElement("content", atom);
                xml.WriteAttributeString("type", "html");
                xml.WriteString(Html(week));
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        });
    }

    // вміст type="html" XmlWriter екранує сам, тут тільки екрануємо текст для HTML
    private static string Html(PeriodChanges week)
    {
        var html = new StringBuilder();
        Part(html, "Усі відкриті картки", week, week.All, null);
        Part(html, "Агентство і служби відновлення", week, week.Services, week.Fastest);
        foreach (string note in ChangeText.Notes(week, PeriodKind.Week)) html.Append("<p>").Append(WebUtility.HtmlEncode(note)).Append("</p>");
        return html.ToString();
    }

    private static void Part(StringBuilder html, string title, PeriodChanges week, ChangeCounts counts, IReadOnlyList<ServiceExample>? fastest)
    {
        html.Append("<h3>").Append(WebUtility.HtmlEncode(title)).Append("</h3>");
        if (ChangeText.Nothing(week, counts, PeriodKind.Week) is { } nothing)
        {
            html.Append("<p>").Append(WebUtility.HtmlEncode(nothing)).Append("</p>");
            return;
        }

        var lines = new List<string>
        {
            "Оновлено карток: " + Format.Number(counts.UpdatedCards),
            "Дозаповнено полів: " + Format.Number(counts.FieldsFilled) + (counts.Fields.Count > 0 ? $" ({ChangeText.FieldList(counts.Fields)})" : ""),
            "Нових карток: " + Format.Number(counts.NewCards),
            "Перейшли на іншу стадію: " + Format.Number(counts.StageMoves) + (counts.Stages.Count > 0 ? $" ({ChangeText.StageList(counts.Stages)})" : ""),
        };
        if (fastest is { Count: > 0 }) lines.Add("Де пішло найшвидше: " + ChangeText.FastestList(fastest));
        html.Append("<ul>");
        foreach (string line in lines) html.Append("<li>").Append(WebUtility.HtmlEncode(line)).Append("</li>");
        html.Append("</ul>");
    }
}
