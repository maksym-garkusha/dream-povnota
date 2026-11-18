using System.Net;
using DreamPovnota.Web.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamPovnota.Web.Tests;

public sealed class WebhookRegistrationTests : IDisposable
{
    private readonly FakeTelegram telegram = new();
    private readonly ServiceProvider services;
    private readonly BotSettings settings;

    public WebhookRegistrationTests() : this("https://povnota.example")
    {
    }

    private WebhookRegistrationTests(string? renderUrl)
    {
        settings = BotSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [BotSettings.TokenName] = TelegramBotTests.Token,
            [BotSettings.RenderUrlName] = renderUrl,
        }).Build());
        var collection = new ServiceCollection();
        collection.AddSingleton(settings);
        collection.AddHttpClient<TelegramClient>(TelegramClient.Configure).ConfigurePrimaryHttpMessageHandler(() => telegram);
        services = collection.BuildServiceProvider();
    }

    private WebhookRegistration Registration() =>
        new(settings, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WebhookRegistration>.Instance)
        {
            Delays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
        };

    [Fact]
    public async Task Webhook_is_set_with_the_secret_and_only_messages()
    {
        bool done = await Registration().RegisterAsync(CancellationToken.None);
        var sent = await telegram.NextAsync();

        Assert.True(done);
        Assert.Equal("https://api.telegram.org/bot" + TelegramBotTests.Token + "/setWebhook", sent.Url);
        Assert.Equal("https://povnota.example/telegram/webhook", sent.Body.GetProperty("url").GetString());
        Assert.Equal(settings.Secret, sent.Body.GetProperty("secret_token").GetString());
        Assert.Equal("message", Assert.Single(sent.Body.GetProperty("allowed_updates").EnumerateArray()).GetString());
        Assert.False(sent.Body.GetProperty("drop_pending_updates").GetBoolean());
    }

    [Fact]
    public async Task Server_errors_and_429_are_retried()
    {
        telegram.FailNext.Enqueue(HttpStatusCode.BadGateway);
        telegram.FailNext.Enqueue(HttpStatusCode.TooManyRequests);

        bool done = await Registration().RegisterAsync(CancellationToken.None);

        Assert.True(done);
        Assert.Equal(3, telegram.Requests);
    }

    [Fact]
    public async Task Rejected_token_is_not_retried()
    {
        telegram.Fail = HttpStatusCode.Unauthorized;

        bool done = await Registration().RegisterAsync(CancellationToken.None);

        Assert.False(done);
        Assert.Equal(1, telegram.Requests);
    }

    [Fact]
    public async Task Registration_gives_up_after_the_last_pause()
    {
        telegram.Fail = HttpStatusCode.ServiceUnavailable;

        bool done = await Registration().RegisterAsync(CancellationToken.None);

        Assert.False(done);
        Assert.Equal(4, telegram.Requests);
    }

    [Fact]
    public async Task Without_a_public_address_nothing_is_sent()
    {
        using var other = new WebhookRegistrationTests(renderUrl: null);

        bool done = await other.Registration().RegisterAsync(CancellationToken.None);

        Assert.False(done);
        Assert.Equal(0, other.telegram.Requests);
    }

    public void Dispose() => services.Dispose();
}
