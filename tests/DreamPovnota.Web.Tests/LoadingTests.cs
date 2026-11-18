using System.Net;

namespace DreamPovnota.Web.Tests;

public class LoadingTests : IDisposable
{
    private readonly WebFactory factory = new() { DataUrl = Path.Combine(Path.GetTempPath(), "no-such-dir", "data.json") };

    public void Dispose()
    {
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Without_an_edition_pages_ask_to_wait_and_refresh_themselves()
    {
        var client = factory.CreateClient();

        var page = await client.GetAsync("/cards");
        var api = await client.GetAsync("/api/v1/meta");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        Assert.NotNull(page.Headers.RetryAfter);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Дані завантажуються", html);
        Assert.Contains("http-equiv=\"refresh\"", html);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_edition()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
