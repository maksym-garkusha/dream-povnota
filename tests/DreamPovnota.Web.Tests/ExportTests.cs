using System.IO.Compression;
using System.Net;
using System.Text;
using DreamPovnota.Web.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DreamPovnota.Web.Tests;

/// <summary>Повні вивантаження, які готуються на випуск, і заголовки для повторних запитів.</summary>
public class ExportTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public ExportTests(WebFactory factory) => this.factory = factory;

    [Fact]
    public async Task Full_export_is_prepared_once_and_answers_304_to_a_known_etag()
    {
        var client = await factory.ReadyClientAsync();

        var first = await client.GetAsync("/export/cards.xlsx");
        var etag = first.Headers.ETag;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/export/cards.xlsx");
        request.Headers.IfNoneMatch.Add(etag!);
        var again = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(etag);
        Assert.NotNull(first.Content.Headers.LastModified);
        Assert.Equal("bytes", first.Headers.AcceptRanges.Single());
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task Page_and_unknown_parameters_do_not_change_the_export()
    {
        var client = await factory.ReadyClientAsync();

        var full = await client.GetAsync("/export/cards.csv");
        var paged = await client.GetAsync("/export/cards.csv?page=7&utm_source=mail");

        Assert.Equal(HttpStatusCode.OK, paged.StatusCode);
        Assert.Equal(full.Headers.ETag, paged.Headers.ETag);
        Assert.Equal(await full.Content.ReadAsByteArrayAsync(), await paged.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Full_csv_goes_out_already_compressed_to_clients_that_accept_gzip()
    {
        var client = await factory.ReadyClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/export/cards.csv");
        request.Headers.AcceptEncoding.ParseAdd("gzip, br");

        var response = await client.SendAsync(request);
        using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var text = new StreamReader(gzip, Encoding.UTF8);

        Assert.Equal("gzip", response.Content.Headers.ContentEncoding.Single());
        Assert.StartsWith("Код DREAM;", await text.ReadLineAsync());
    }

    [Theory]
    [InlineData("/export/cards.csv")]
    [InlineData("/export/cards.xlsx")]
    [InlineData("/api/v1/cards.xlsx?stage=impl")]
    public async Task Head_on_an_export_answers_without_a_body(string path)
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Interrupted_full_download_can_be_resumed_with_a_range()
    {
        var client = await factory.ReadyClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/export/cards.xlsx");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("PK"u8.ToArray(), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Shared_build_limit_is_spent_only_on_builds_not_on_cache_hits_or_the_full_file()
    {
        using var limited = new WebFactory { Replace = s => s.AddSingleton(new SharedLimits(exportBuildsPerMinute: 1)) };
        var client = await limited.ReadyClientAsync();

        var built = await client.GetAsync("/export/cards.xlsx?stage=impl");
        var cached = await client.GetAsync("/export/cards.xlsx?stage=impl");
        var full = await client.GetAsync("/export/cards.xlsx");
        var refused = await client.GetAsync("/export/cards.xlsx?stage=idea");

        Assert.Equal(HttpStatusCode.OK, built.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(refused.Headers.RetryAfter);
    }
}
