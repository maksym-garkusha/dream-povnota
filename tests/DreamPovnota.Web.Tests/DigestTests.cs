using System.Net;
using System.Security.Cryptography;
using DreamPovnota.Digest;
using DreamPovnota.Export;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Сторінки й файли зрізу для служб відновлення на справжньому data.json, обрізаному
/// до агентства і служб у Вінницькій та Черкаській областях (digest-edition.json).
/// </summary>
public class DigestTests : IClassFixture<WebFactory>
{
    private static readonly string EditionPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "digest-edition.json");

    private readonly WebFactory factory;

    public DigestTests(WebFactory factory)
    {
        this.factory = factory;
        factory.DataUrl = EditionPath;
    }

    private static async Task<LoadedEdition> LoadedAsync(Func<Edition, Edition>? change = null)
    {
        byte[] body = await File.ReadAllBytesAsync(EditionPath);
        var edition = await Edition.ReadAsync(new MemoryStream(body));
        return new LoadedEdition(change is null ? edition : change(edition), SHA256.HashData(Guid.NewGuid().ToByteArray()));
    }

    [Fact]
    public async Task List_shows_every_organisation_in_the_fixed_order_and_is_not_indexed()
    {
        var client = await factory.ReadyClientAsync();

        string html = await client.GetStringAsync("/digest");

        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", html);
        var positions = DigestOrganisations.All.Select(o => html.IndexOf("/digest/" + o.Edrpou + "\"", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p > 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("Не пов'язаний з державними органами", html);
    }

    [Fact]
    public async Task Organisation_page_shows_the_three_numbers_and_both_files()
    {
        var client = await factory.ReadyClientAsync();

        string html = await client.GetStringAsync("/digest/25845655");

        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", html);
        Assert.Contains("<b>16</b><span>відкритих карток</span>", html);
        Assert.Contains("<b>14</b><span>заповнено все доречне</span>", html);
        Assert.Contains("<b>0</b><span>оновлено за тиждень</span>", html);
        Assert.Contains("href=\"/digest/25845655.xlsx\"", html);
        Assert.Contains("href=\"/digest/25845655.pdf\"", html);
        Assert.Contains("Історія змін зараз недоступна", html);
    }

    [Theory]
    [InlineData("/digest/12345678")]
    [InlineData("/digest/12345678.xlsx")]
    [InlineData("/digest/12345678.pdf")]
    [InlineData("/digest/2584565")]
    public async Task Organisation_outside_the_list_is_not_found(string path)
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK")]
    [InlineData("pdf", "application/pdf", "%PDF")]
    public async Task File_is_built_once_and_answers_304_to_a_known_etag(string format, string type, string magic)
    {
        var client = await factory.ReadyClientAsync();

        var first = await client.GetAsync("/digest/05422303." + format);
        byte[] body = await first.Content.ReadAsByteArrayAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/digest/05422303." + format);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var again = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(type, first.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"zriz-05422303-2026-11-18.{format}", first.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("noindex, nofollow", first.Headers.GetValues("X-Robots-Tag").Single());
        Assert.StartsWith(magic, System.Text.Encoding.ASCII.GetString(body, 0, magic.Length));
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task Service_prints_the_hash_of_the_edition_it_accepted_in_both_files()
    {
        var client = await factory.ReadyClientAsync();
        string prefix = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(EditionPath)))[..16];

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(await client.GetByteArrayAsync("/digest/25845655.pdf"));
        var sheet = MiniExcelLibs.MiniExcel.Query(new MemoryStream(await client.GetByteArrayAsync("/digest/25845655.xlsx")), useHeaderRow: true, sheetName: DigestWorkbook.SummarySheet)
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(row => (string)row["Показник"]!, row => row["Значення"]);

        Assert.Contains(prefix, string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)), StringComparison.Ordinal);
        Assert.Equal(prefix, sheet["SHA-256 data.json, перші 16 знаків"]);
    }

    [Fact]
    public async Task Baseline_without_a_database_is_unavailable_and_checks_its_date()
    {
        var client = await factory.ReadyClientAsync();

        var unavailable = await client.GetAsync("/api/v1/digest/baseline");
        var wrong = await client.GetAsync("/api/v1/digest/baseline?from=2026-13-01");
        var future = await client.GetAsync("/api/v1/digest/baseline?from=2026-12-01");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task Pdf_downloads_share_the_export_limit()
    {
        await using var own = new WebFactory { DataUrl = EditionPath };
        var client = await own.ReadyClientAsync();

        for (int i = 0; i < Web.Limits.ExportsPerMinute; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/digest/05422303.pdf")).StatusCode);
        var refused = await client.GetAsync("/digest/05422303.pdf");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [PostgresFact]
    public async Task Baseline_is_the_last_snapshot_before_the_week_and_the_cli_reads_it_back()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        var current = await LoadedAsync();
        int documents = 1 << current.Edition.Checks.ToList().FindIndex(c => c.Code == "documents");
        // знімок десятиденної давності: у картці Черкаської служби ще не було документів;
        // і ще один, уже всередині тижня, який для порівняння не годиться
        var older = await LoadedAsync(e => e with
        {
            Generated = e.Generated.AddDays(-10),
            Cards = e.Cards.Select(c => e.Organisations[c.Organisation].Edrpou == "05422303" ? c with { Filled = c.Filled & ~documents } : c).ToList(),
        });
        var inside = await LoadedAsync(e => e with { Generated = e.Generated.AddDays(-2) });
        await SnapshotWriter.WriteAsync(database.Db, older, CancellationToken.None);
        await SnapshotWriter.WriteAsync(database.Db, inside, CancellationToken.None);
        await SnapshotWriter.WriteAsync(database.Db, current, CancellationToken.None);

        await using var web = new WebFactory { DataUrl = EditionPath, DatabaseUrl = database.Url };
        var client = await web.ReadyClientAsync();
        var baseline = await new DigestHistoryClient(client).FetchAsync(client.BaseAddress!.ToString(), DigestBuilder.WeekFrom(current.Edition), CancellationToken.None);
        var digest = DigestBuilder.Build(current.Edition, current.Sha256, DigestOrganisations.Find("05422303")!, baseline);
        string html = await client.GetStringAsync("/digest/05422303");

        // PostgreSQL зберігає час з точністю до мікросекунди
        Assert.Equal(older.Edition.Generated, baseline.Taken, TimeSpan.FromMilliseconds(1));
        Assert.Equal(older.Edition.Generated, baseline.HistoryFrom, TimeSpan.FromMilliseconds(1));
        Assert.Equal(26, baseline.Cards.Count);
        Assert.Equal(current.Edition.Checks.Select(c => c.Code), baseline.Checks);
        Assert.Equal(HistoryCoverage.FullWeek, digest.History);
        Assert.Equal(["Документи або фото"], Assert.Single(digest.Week).NowFilled);
        Assert.Contains("Дозаповнено полів: 1.", html);
        Assert.Contains("Порівняння зі станом карток на 08.11.2026.", html);
    }
}
