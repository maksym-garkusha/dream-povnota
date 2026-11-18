using System.Text.Json;
using DreamPovnota.Web.Telegram;
using Microsoft.Extensions.Configuration;

namespace DreamPovnota.Web.Tests;

public class BotPartsTests
{
    private static BotSettings Settings(string? token, string? secret, string? site = null, string? webhookUrl = null, string? renderUrl = null) =>
        BotSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [BotSettings.TokenName] = token,
            [BotSettings.SecretName] = secret,
            [BotSettings.SiteName] = site,
            [BotSettings.WebhookUrlName] = webhookUrl,
            [BotSettings.RenderUrlName] = renderUrl,
        }).Build());

    [Fact]
    public void Bot_is_off_and_quiet_without_a_token()
    {
        var settings = Settings(null, TelegramBotTests.Secret);

        Assert.False(settings.Enabled);
        Assert.Null(settings.Problem);
    }

    [Theory]
    [InlineData(TelegramBotTests.Token, "short")]
    [InlineData(TelegramBotTests.Token, "has spaces in the secret value")]
    [InlineData("not-a-token", TelegramBotTests.Secret)]
    [InlineData("123:abc/../../evil-path-abcdefghij", TelegramBotTests.Secret)]
    public void Bot_is_off_with_a_reason_when_token_or_secret_look_wrong(string token, string? secret)
    {
        var settings = Settings(token, secret);

        Assert.False(settings.Enabled);
        Assert.NotNull(settings.Problem);
        Assert.DoesNotContain(token, settings.Problem);
        Assert.Equal("", settings.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_secret_it_is_derived_from_the_token(string? secret)
    {
        var settings = Settings(TelegramBotTests.Token, secret);

        Assert.True(settings.Enabled);
        Assert.Equal(BotSettings.DeriveSecret(TelegramBotTests.Token), settings.Secret);
        Assert.Matches("^[0-9a-f]{64}$", settings.Secret);
    }

    [Fact]
    public void Derived_secret_is_stable_and_differs_between_tokens()
    {
        string first = BotSettings.DeriveSecret(TelegramBotTests.Token);

        Assert.Equal(first, BotSettings.DeriveSecret(TelegramBotTests.Token));
        Assert.NotEqual(first, BotSettings.DeriveSecret("123456:another-test-token-000000"));
        Assert.DoesNotContain("test-token", first);
    }

    // те саме значення дає команда openssl з README для ручного setWebhook
    [Fact]
    public void Derived_secret_matches_the_openssl_command_in_readme()
    {
        Assert.Equal("e91c6bd2e3332c884421fe8bd886da3180ece592ce4368464dd1335aecdba163", BotSettings.DeriveSecret(TelegramBotTests.Token));
    }

    [Fact]
    public void Explicit_secret_wins_over_the_derived_one()
    {
        Assert.Equal(TelegramBotTests.Secret, Settings(TelegramBotTests.Token, TelegramBotTests.Secret).Secret);
    }

    [Theory]
    [InlineData("https://bot.example", null, "https://bot.example/telegram/webhook")]
    [InlineData("https://bot.example/telegram/webhook", null, "https://bot.example/telegram/webhook")]
    [InlineData(null, "https://dream-povnota.onrender.com", "https://dream-povnota.onrender.com/telegram/webhook")]
    [InlineData("https://bot.example", "https://dream-povnota.onrender.com", "https://bot.example/telegram/webhook")]
    [InlineData("http://bot.example", "https://dream-povnota.onrender.com", "https://dream-povnota.onrender.com/telegram/webhook")]
    [InlineData(null, null, null)]
    [InlineData("http://bot.example", null, null)]
    public void Webhook_address_comes_from_the_variable_or_from_render(string? webhookUrl, string? renderUrl, string? expected)
    {
        var settings = Settings(TelegramBotTests.Token, null, webhookUrl: webhookUrl, renderUrl: renderUrl);

        Assert.Equal(expected, settings.WebhookUrl?.ToString());
    }

    [Fact]
    public void Bot_is_on_with_both_values()
    {
        var settings = Settings(TelegramBotTests.Token, TelegramBotTests.Secret);

        Assert.True(settings.Enabled);
        Assert.Equal(BotSettings.DefaultSite, settings.Site);
    }

    [Theory]
    [InlineData("https://povnota.example/", "https://povnota.example/")]
    [InlineData("http://povnota.example/", "https://dream-povnota.onrender.com/")]
    [InlineData("not a url", "https://dream-povnota.onrender.com/")]
    public void Site_for_links_must_be_https(string site, string expected)
    {
        Assert.Equal(new Uri(expected), Settings(TelegramBotTests.Token, TelegramBotTests.Secret, site).Site);
    }

    [Theory]
    [InlineData(TelegramBotTests.Secret, true)]
    [InlineData("test-webhook-secret-0002", false)]
    [InlineData("test-webhook-secret-000", false)]
    [InlineData("", false)]
    public void Secret_is_compared_exactly(string given, bool same)
    {
        Assert.Equal(same, TelegramBot.SameSecret(given, TelegramBotTests.Secret));
    }

    [Fact]
    public void Private_text_message_is_read()
    {
        var job = Read("""{"update_id":1,"message":{"chat":{"id":42,"type":"private"},"text":"DREAM-UA-050923-B400F7B2"}}""");

        Assert.Equal(new BotJob(42, "DREAM-UA-050923-B400F7B2"), job);
    }

    [Fact]
    public void Message_without_text_becomes_empty_text()
    {
        Assert.Equal("", Read("""{"message":{"chat":{"id":42,"type":"private"},"photo":[]}}""")!.Text);
    }

    [Theory]
    [InlineData("""{"message":{"chat":{"id":-100,"type":"group"},"text":"/check DREAM-UA-050923-B400F7B2"}}""")]
    [InlineData("""{"message":{"chat":{"id":-100,"type":"supergroup"},"text":"привіт"}}""")]
    [InlineData("""{"channel_post":{"chat":{"id":-100,"type":"channel"},"text":"DREAM-UA-050923-B400F7B2"}}""")]
    [InlineData("""{"edited_message":{"chat":{"id":42,"type":"private"},"text":"DREAM-UA-050923-B400F7B2"}}""")]
    [InlineData("""{"message":{"chat":{"id":"42","type":"private"},"text":"x"}}""")]
    [InlineData("""{"message":{"chat":{"type":"private"},"text":"x"}}""")]
    [InlineData("""{"message":"x"}""")]
    [InlineData("[]")]
    public void Anything_but_a_private_message_is_ignored(string json)
    {
        Assert.Null(Read(json));
    }

    [Fact]
    public void Chat_gets_six_answers_a_minute_then_one_notice_then_silence()
    {
        using var limits = new ChatLimits();

        var verdicts = Enumerable.Range(0, ChatLimits.PerMinute + 2).Select(_ => limits.Check(42).Verdict).ToList();

        Assert.All(verdicts.Take(ChatLimits.PerMinute), v => Assert.Equal(ChatVerdict.Allowed, v));
        Assert.Equal(ChatVerdict.Notify, verdicts[ChatLimits.PerMinute]);
        Assert.Equal(ChatVerdict.Drop, verdicts[ChatLimits.PerMinute + 1]);
        Assert.Equal(ChatVerdict.Allowed, limits.Check(43).Verdict);
    }

    [Fact]
    public void Hourly_limit_holds_even_when_the_minute_one_has_room()
    {
        using var limits = new ChatLimits(perMinute: 100, perHour: 3);

        for (int i = 0; i < 3; i++) Assert.Equal(ChatVerdict.Allowed, limits.Check(42).Verdict);
        var (verdict, wait) = limits.Check(42);

        Assert.Equal(ChatVerdict.Notify, verdict);
        Assert.True(wait > TimeSpan.Zero);
    }

    [Fact]
    public void Full_queue_refuses_new_messages()
    {
        var queue = new BotQueue(capacity: 2);

        Assert.True(queue.TryAdd(new BotJob(1, "a")));
        Assert.True(queue.TryAdd(new BotJob(2, "b")));
        Assert.False(queue.TryAdd(new BotJob(3, "c")));
    }

    private static BotJob? Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return TelegramBot.Read(document.RootElement);
    }
}
