using System.Text;
using System.Xml;
using DreamPovnota.Web.Changes;
using DreamPovnota.Web.Feeds;
using DreamPovnota.Web.History;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Стрічки віддаються в UTF-8, тож і в оголошенні XML має стояти utf-8: парсер, який вірить
/// оголошенню, на encoding="utf-16" без BOM відмовляється читати файл.
/// </summary>
public class FeedEncodingTests
{
    private static readonly DateTimeOffset Generated = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    // так само, як Results.Text(xml, ContentType, Encoding.UTF8): байти UTF-8 без BOM
    private static XmlDocument ParseAsServed(string xml)
    {
        var document = new XmlDocument();
        document.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        return document;
    }

    [Fact]
    public void Organisation_feed_declares_the_utf8_it_is_served_in()
    {
        SnapshotInfo[] snapshots = [new(2, Generated, Generated, 3, 0, 0, 1, false), new(1, Generated.AddDays(-1), null, 3, 3, 0, 0, false)];
        CardChange[] changes = [new(2, "010101-ABCDEF12", "Школа «Сонечко»", ChangeKind.Changed, "idea", "idea", ["sector"], [], [])];

        string xml = AtomFeed.Build("https://example.org", "example.org", "org", "12345678", "Громада", "/org/12345678", snapshots, changes,
            new Dictionary<string, string> { ["sector"] = "Сектор", ["idea"] = "Ідея" });

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        var document = ParseAsServed(xml);
        Assert.Contains("Сектор", document.DocumentElement!.InnerText);
    }

    [Fact]
    public void Changes_feed_declares_the_utf8_it_is_served_in()
    {
        var summary = new ChangeSummary(Generated, Generated, 1, null, [], []);

        string xml = ChangesFeed.Build("https://example.org", "example.org", summary);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Equal("Рух у картках DREAM за тижнями", ParseAsServed(xml).GetElementsByTagName("title")[0]!.InnerText);
    }
}
