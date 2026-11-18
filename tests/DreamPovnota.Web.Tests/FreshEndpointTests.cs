using System.Net;
using System.Text.Json;
using DreamPovnota.Web.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DreamPovnota.Web.Tests;

/// <summary>/health/fresh у цілому застосунку. Годинник підставний: випуск з фікстур зібрано 17.11.2026.</summary>
public sealed class FreshEndpointTests : IDisposable
{
    // година після збірки випуску
    private readonly FakeTimeProvider clock = new(Fixtures.AsOf.AddHours(1));
    private readonly WebFactory factory;

    public FreshEndpointTests() => factory = new WebFactory { Replace = services => services.AddSingleton<TimeProvider>(clock) };

    public void Dispose() => factory.Dispose();

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync("/health/fresh");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone(), response);
    }

    [Fact]
    public async Task Fresh_edition_answers_200_with_a_short_body_that_is_neither_cached_nor_indexed()
    {
        var client = await factory.ReadyClientAsync();

        var (status, body, response) = await GetAsync(client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["fresh", "reason", "lastSuccessfulCheck", "editionAgeHours"], body.EnumerateObject().Select(p => p.Name));
        Assert.True(body.GetProperty("fresh").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reason").ValueKind);
        Assert.Equal(clock.GetUtcNow(), body.GetProperty("lastSuccessfulCheck").GetDateTimeOffset());
        Assert.Equal(1.0, body.GetProperty("editionAgeHours").GetDouble());
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Held_edition_is_503_here_while_ready_still_answers_200()
    {
        var client = await factory.ReadyClientAsync();
        // так імпорт записує файл, який не прийняв запобіжник
        factory.Services.GetRequiredService<EditionStore>().Held(clock.GetUtcNow(), "EditionGuard: changed-cards");

        var (status, body, _) = await GetAsync(client);
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("fresh").GetBoolean());
        Assert.Equal("guard-held", body.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Edition_not_renewed_for_a_day_and_a_half_is_503_stale()
    {
        var client = await factory.ReadyClientAsync();

        clock.Advance(TimeSpan.FromHours(36));
        // так імпорт записує відповідь 304; файл з фікстур не змінився, тож і сам він запише те саме
        factory.Services.GetRequiredService<EditionStore>().Checked(clock.GetUtcNow());
        var (status, body, response) = await GetAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("stale", body.GetProperty("reason").GetString());
        Assert.Equal(37.0, body.GetProperty("editionAgeHours").GetDouble());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Limit_is_read_from_the_freshness_settings()
    {
        factory.Settings["Freshness:EditionAgeHours"] = "72";
        var client = await factory.ReadyClientAsync();

        clock.Advance(TimeSpan.FromHours(36));
        factory.Services.GetRequiredService<EditionStore>().Checked(clock.GetUtcNow());
        var (status, _, _) = await GetAsync(client);

        Assert.Equal(HttpStatusCode.OK, status);
    }
}
