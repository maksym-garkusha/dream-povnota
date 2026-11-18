using System.Net;

namespace DreamPovnota.Web.Tests;

public class LiveCheckPageTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public LiveCheckPageTests(WebFactory factory)
    {
        this.factory = factory;
        factory.Dream.AnyObject = Fixtures.Text("object-active.json");
        factory.Dream.Json("ideas/" + PagesTests.ActiveCode, Fixtures.Text("project-active.json"));
    }

    private static FormUrlEncodedContent Form(string code, string token) =>
        new([new("code", code), new("__RequestVerificationToken", token)]);

    [Fact]
    public async Task Successful_check_redirects_to_the_result_and_the_seventh_in_a_minute_is_refused()
    {
        var client = await factory.ReadyClientAsync();
        string token = await PagesTests.TokenAsync(client);
        string address = "https://dream.gov.ua/ua/project/DREAM-UA-" + PagesTests.ActiveCode + "/profile";

        var first = await client.PostAsync("/check", Form(address, token));
        Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
        Assert.Equal("/check/" + PagesTests.ActiveCode, first.Headers.Location!.OriginalString);

        string result = await client.GetStringAsync(first.Headers.Location);
        Assert.Contains("доречних полів заповнено", result);

        HttpResponseMessage last = first;
        for (int i = 2; i <= 7; i++) last = await client.PostAsync("/check", Form(PagesTests.ActiveCode, token));

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.True(last.Headers.RetryAfter is not null);
        Assert.Contains("Забагато запитів", await last.Content.ReadAsStringAsync());
    }
}
