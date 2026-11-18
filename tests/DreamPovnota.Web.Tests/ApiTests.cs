using System.Net;
using System.Text;
using System.Text.Json;
using DreamPovnota.Web.Lists;
using MiniExcelLibs;

namespace DreamPovnota.Web.Tests;

public class ApiTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public ApiTests(WebFactory factory) => this.factory = factory;

    [Fact]
    public async Task Cards_list_has_the_documented_shape()
    {
        var client = await factory.ReadyClientAsync();
        var edition = await Fixtures.LoadedAsync();

        var response = await client.GetAsync("/api/v1/cards?pageSize=2");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(edition.Edition.Cards.Count, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
        var item = root.GetProperty("items")[0];
        Assert.Equal(edition.Edition.Cards[0].Code, item.GetProperty("code").GetString());
        Assert.StartsWith("https://dream.gov.ua/ua/project/DREAM-UA-", item.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Object, item.GetProperty("org").ValueKind);
        Assert.Equal(JsonValueKind.Array, item.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task Wrong_parameter_gives_a_validation_problem()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/api/v1/cards?pageSize=501");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("pageSize", out _));
    }

    [Fact]
    public async Task Unknown_card_is_a_not_found_problem()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/api/v1/cards/010101-ABCDEF12");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Openapi_document_lists_the_public_endpoints()
    {
        var client = await factory.ReadyClientAsync();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");

        Assert.StartsWith("3.", json.RootElement.GetProperty("openapi").GetString());
        foreach (string path in new[] { "/api/v1/cards", "/api/v1/meta", "/api/v1/regions", "/api/v1/cards.csv" })
            Assert.True(paths.TryGetProperty(path, out _), path);
        Assert.False(paths.TryGetProperty("/export/cards.csv", out _));
    }

    [Fact]
    public async Task Csv_export_opens_in_excel_with_cyrillic()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/export/cards.csv");
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, body[..3]);
        string header = Encoding.UTF8.GetString(body[3..]).Split("\r\n")[0];
        Assert.StartsWith("Код DREAM;Посилання;Назва;Організація;ЄДРПОУ", header);
    }

    [Fact]
    public async Task Xlsx_export_has_the_cards_sheet_with_every_filtered_card()
    {
        var client = await factory.ReadyClientAsync();
        var edition = await Fixtures.LoadedAsync();
        int impl = edition.StageIndex["impl"];
        int expected = edition.Edition.Cards.Count(c => c.Stage == impl);

        var response = await client.GetAsync("/export/cards.xlsx?stage=impl");
        using var body = new MemoryStream(await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Картки"], MiniExcel.GetSheetNames(body));
        Assert.Equal(ExportRows.Headers(edition), MiniExcel.GetColumns(body, useHeaderRow: true));
        var rows = MiniExcel.Query(body, useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();
        Assert.True(expected > 0);
        Assert.Equal(expected, rows.Count);
        Assert.All(rows, row => Assert.Equal("Реалізація", row["Стадія"]));
        Assert.All(rows, row => Assert.IsType<double>(row["Наповненість, %"]));
        // перевірка, недоречна на стадії, лишає клітинку порожньою, а не «ні»
        Assert.Contains(rows, row => edition.Edition.Checks.Any(c => row[c.Title] is null));
    }

    [Fact]
    public async Task Meta_lists_checks_and_regions_from_the_edition()
    {
        var client = await factory.ReadyClientAsync();
        var edition = await Fixtures.LoadedAsync();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/meta"));

        Assert.Equal(edition.Edition.Checks.Count, json.RootElement.GetProperty("checks").GetArrayLength());
        Assert.Equal(edition.Edition.Regions.Count, json.RootElement.GetProperty("regions").GetArrayLength());
        // той самий хеш, що в sha256sum data.json і в атестації site.yml
        string sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Fixtures.EditionPath)));
        Assert.Equal(sha256, json.RootElement.GetProperty("edition").GetProperty("sha256").GetString());
    }
}
