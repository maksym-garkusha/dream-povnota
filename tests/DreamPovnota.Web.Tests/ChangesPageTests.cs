using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DreamPovnota.Export;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Tests;

public partial class ChangesPageTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public ChangesPageTests(WebFactory factory) => this.factory = factory;

    [GeneratedRegex("<svg .*?</svg>", RegexOptions.Singleline)]
    private static partial Regex SvgPattern();

    [Fact]
    public async Task Without_history_the_page_explains_and_the_api_and_feed_answer_503()
    {
        var client = await factory.ReadyClientAsync();

        var page = await client.GetAsync("/changes");
        var api = await client.GetAsync("/api/v1/changes/summary");
        var feed = await client.GetAsync("/changes.atom");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Історія змін тимчасово недоступна", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, feed.StatusCode);
    }

    [Fact]
    public async Task Home_and_digest_link_to_the_changes()
    {
        var client = await factory.ReadyClientAsync();

        Assert.Contains("href=\"/changes\"", await client.GetStringAsync("/"));
        Assert.Contains("href=\"/changes#services\"", await client.GetStringAsync("/digest"));
    }

    [Fact]
    public async Task Summary_is_in_the_openapi_document_next_to_the_snapshot_changes()
    {
        var client = await factory.ReadyClientAsync();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/v1/changes/summary", out _));
        Assert.True(paths.TryGetProperty("/api/v1/changes", out _));
    }

    // знімок 1 у вівторок 10.11 з порожнім полем у картці a і старішою датою в картці b,
    // далі сервіс сам імпортує випуск з фікстур (18.11, 00:00 за Києвом) як знімок 2
    [PostgresFact]
    public async Task Page_api_and_feed_show_the_weeks_from_the_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        var edition = (await Fixtures.LoadedAsync()).Edition;
        int a = Enumerable.Range(0, edition.Cards.Count).First(i => FilledField(edition, edition.Cards[i]) >= 0);
        int bit = FilledField(edition, edition.Cards[a]);
        var cards = edition.Cards.ToList();
        cards[a] = cards[a] with { Filled = cards[a].Filled & ~(1 << bit) };
        int b = a == 0 ? 1 : 0;
        cards[b] = cards[b] with { Updated = cards[b].Updated.AddDays(-5) };
        var older = edition with { Generated = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), Cards = cards };
        await SnapshotWriter.WriteAsync(database.Db, new LoadedEdition(older, SHA256.HashData(Guid.NewGuid().ToByteArray())), CancellationToken.None);

        await using var app = new WebFactory { DatabaseUrl = database.Url };
        var client = await app.ReadyClientAsync();
        JsonElement summary = default;
        for (int i = 0; i < 50; i++)
        {
            var response = await client.GetAsync("/api/v1/changes/summary");
            if (response.IsSuccessStatusCode)
            {
                summary = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                if (summary.GetProperty("snapshots").GetInt32() == 2) break;
            }
            await Task.Delay(100);
        }

        Assert.Equal(2, summary.GetProperty("snapshots").GetInt32());
        var weeks = summary.GetProperty("weeks");
        Assert.Equal(["2026-W46", "2026-W47"], weeks.EnumerateArray().Select(w => w.GetProperty("isoWeek").GetString()));
        var w47 = weeks[1].GetProperty("all");
        Assert.Equal(1, w47.GetProperty("updatedCards").GetInt32());
        Assert.Equal(1, w47.GetProperty("fieldsFilled").GetInt32());
        Assert.Equal(edition.Checks[bit].Code, w47.GetProperty("fields")[0].GetProperty("check").GetString());
        Assert.Equal(JsonValueKind.Null, weeks[0].GetProperty("comparedFrom").ValueKind);
        Assert.Equal(6, summary.GetProperty("firstWeekDays").GetArrayLength());

        var page = await client.GetAsync("/changes");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain(" style=", html);
        Assert.Contains("<link rel=\"alternate\" type=\"application/atom+xml\" title=\"Рух у картках DREAM за тижнями\" href=\"/changes.atom\">", html);
        Assert.Contains("Тут тільки перший знімок історії, з ним порівнюються наступні.", html);
        var svgs = SvgPattern().Matches(html).Select(m => XElement.Parse(m.Value)).ToList();
        Assert.All(svgs, ColumnChartTests.AssertSelfContained);
        // чотири графіки за тижнями, два для служб, чотири за днями першого тижня; решта смужки в таблиці полів
        Assert.Equal(10, svgs.Count(s => (string?)s.Attribute("class") == "chart"));
        Assert.Contains("<b>1</b><span>дозаповнене поле</span>", html);

        var feed = XDocument.Parse(await client.GetStringAsync("/changes.atom"));
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entries = feed.Root!.Elements(atom + "entry").ToList();
        Assert.Equal(2, entries.Count);
        Assert.EndsWith("changes/2026-W47", entries[0].Element(atom + "id")!.Value);
        Assert.Contains("Дозаповнено полів: 1", entries[0].Element(atom + "content")!.Value);
    }

    private static int FilledField(Edition edition, EditionCard card)
    {
        for (int bit = 0; bit < edition.Checks.Count; bit++)
            if ((card.Filled & 1 << bit) != 0 && edition.Checks[bit].Code != "fresh") return bit;
        return -1;
    }
}
