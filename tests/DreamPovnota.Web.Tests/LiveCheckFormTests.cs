using System.Net;
using DreamPovnota.Web.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DreamPovnota.Web.Tests;

public class LiveCheckFormTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public LiveCheckFormTests(WebFactory factory) => this.factory = factory;

    [Fact]
    public async Task Address_on_another_host_is_refused_without_calling_dream()
    {
        var client = await factory.ReadyClientAsync();
        string token = await PagesTests.TokenAsync(client);

        var response = await client.PostAsync("/check", new FormUrlEncodedContent([
            new("code", "https://dream.gov.ua.evil.com/ua/project/DREAM-UA-050923-B400F7B2"),
            new("__RequestVerificationToken", token),
        ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Не схоже на код DREAM", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Dream.Requests);
    }

    [Fact]
    public async Task Code_unknown_to_dream_is_reported_next_to_the_field()
    {
        var client = await factory.ReadyClientAsync();
        string token = await PagesTests.TokenAsync(client);

        var response = await client.PostAsync("/check", new FormUrlEncodedContent([
            new("code", "DREAM-UA-010101-ABCDEF12"),
            new("__RequestVerificationToken", token),
        ]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("у DREAM немає", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Posts_without_a_form_token_do_not_use_up_the_shared_limit()
    {
        using var limited = new WebFactory { Replace = s => s.AddSingleton(new SharedLimits(checksPerHour: 1)) };
        var client = await limited.ReadyClientAsync();

        for (int i = 0; i < 5; i++)
        {
            var junk = await client.PostAsync("/check", new FormUrlEncodedContent([new("code", "DREAM-UA-010101-ABCDEF1" + i)]));
            Assert.Equal(HttpStatusCode.BadRequest, junk.StatusCode);
        }
        string token = await PagesTests.TokenAsync(client);
        var honest = await client.PostAsync("/check", new FormUrlEncodedContent([
            new("code", "DREAM-UA-010101-ABCDEF12"),
            new("__RequestVerificationToken", token),
        ]));

        Assert.Equal(HttpStatusCode.NotFound, honest.StatusCode);
        Assert.True(limited.Dream.Requests > 0);
    }
}
