using System.Net;
using System.Text.Json;

namespace DreamPovnota.Web.Tests;

/// <summary>Заголовки безпеки, robots.txt, favicon.ico і відповіді API на помилки.</summary>
public class HeadersTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public HeadersTests(WebFactory factory) => this.factory = factory;

    // так запит доходить до сервісу на Render: https знімає проксі й лишає схему в заголовку
    private static HttpRequestMessage BehindProxy(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = "dream-povnota.example";
        request.Headers.Add("X-Forwarded-Proto", "https");
        return request;
    }

    [Fact]
    public async Task Form_cookie_is_secure_and_hsts_lasts_a_year_without_preload()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.SendAsync(BehindProxy("/check"));

        string cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dp-af=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("max-age=31536000", response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task Robots_keep_crawlers_away_from_exports_api_and_live_check()
    {
        var client = await factory.ReadyClientAsync();

        string robots = await client.GetStringAsync("/robots.txt");
        string home = await client.GetStringAsync("/");

        Assert.Contains("Disallow: /export/", robots);
        Assert.Contains("Disallow: /api/", robots);
        Assert.Contains("Disallow: /check", robots);
        Assert.Contains("<a href=\"/export/cards.xlsx\" rel=\"nofollow\">", home);
    }

    [Fact]
    public async Task Favicon_ico_is_there_for_clients_that_do_not_read_html()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/favicon.ico");
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, body[..4]);
    }

    [Fact]
    public async Task Unknown_api_address_is_a_problem_in_json_even_for_a_browser()
    {
        var client = await factory.ReadyClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/nope");
        request.Headers.Accept.ParseAdd("text/html");

        var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Unknown_page_outside_the_api_is_still_an_html_page()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
    }
}
