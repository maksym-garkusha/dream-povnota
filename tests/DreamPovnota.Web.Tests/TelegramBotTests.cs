using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DreamPovnota.Cards;
using DreamPovnota.Web.Telegram;
using DreamPovnota.Web.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DreamPovnota.Web.Tests;

public class TelegramBotTests
{
    // вигадані значення лише для тестів; справжні живуть тільки в змінних середовища Render
    public const string Token = "123456:test-token-not-a-real-one";
    public const string Secret = "test-webhook-secret-0001";
    private const long ChatId = 987654321;

    private static readonly string Active = Fixtures.Text("project-active.json");
    private static readonly string Code = CardParser.ParseProject(Active).Code["DREAM-UA-".Length..];

    private static WebFactory Bot(Action<IServiceCollection>? replace = null)
    {
        var factory = new WebFactory { Replace = replace };
        factory.Settings[BotSettings.TokenName] = Token;
        factory.Settings[BotSettings.SecretName] = Secret;
        factory.Dream.AnyObject = Fixtures.Text("object-active.json");
        factory.Dream.Json("ideas/" + Code, Active);
        return factory;
    }

    // те, що Telegram надсилає на вебхук, без зайвих полів
    private static string Update(string text, string chatType = "private", long chatId = ChatId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 7,
                date = 1763452800,
                from = new { id = chatId, is_bot = false, first_name = "Тест" },
                chat = new { id = chatId, type = chatType },
                text,
            },
        });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string? header = Secret, string path = "/telegram/" + Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (header is not null) request.Headers.Add(TelegramBot.SecretHeader, header);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Without_a_token_the_webhook_does_not_exist()
    {
        using var factory = new WebFactory();
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, Update("/start"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(factory.Services.GetRequiredService<BotSettings>().Enabled);
    }

    [Fact]
    public async Task Token_without_a_secret_keeps_the_bot_off()
    {
        using var factory = new WebFactory();
        factory.Settings[BotSettings.TokenName] = Token;
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, Update("/start"), header: "");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_secret_in_the_path_is_not_found_and_sends_nothing()
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, Update("/start"), path: "/telegram/" + Secret + "x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(factory.Telegram.TryNext(out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("test-webhook-secret-0002")]
    public async Task Missing_or_wrong_secret_header_is_refused(string? header)
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, Update("/start"), header: header);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(factory.Telegram.TryNext(out _));
    }

    [Fact]
    public async Task Start_is_answered_with_help_as_plain_text()
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, Update("/start"));
        var sent = await factory.Telegram.NextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://api.telegram.org/bot" + Token + "/sendMessage", sent.Url);
        Assert.Equal(ChatId, sent.ChatId);
        Assert.Contains(BotReply.Disclaimer, sent.Text);
        Assert.False(sent.Body.TryGetProperty("parse_mode", out _));
        Assert.True(sent.Body.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
    }

    [Fact]
    public async Task Code_is_checked_in_dream_and_answered()
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        await PostAsync(client, Update("https://dream.gov.ua/ua/project/DREAM-UA-" + Code + "/profile"));
        var sent = await factory.Telegram.NextAsync();

        Assert.Contains("DREAM-UA-" + Code, sent.Text);
        Assert.Contains("доречних полів", sent.Text);
        Assert.Contains("Картка в DREAM: https://dream.gov.ua/ua/project/DREAM-UA-" + Code + "/profile", sent.Text);
        Assert.Contains("https://dream-povnota.onrender.com/org/", sent.Text);
        Assert.True(factory.Dream.Requests > 0);
    }

    [Fact]
    public async Task Junk_gets_a_format_hint_without_asking_dream()
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        await PostAsync(client, Update("привіт"));
        var sent = await factory.Telegram.NextAsync();

        Assert.Contains("Не схоже на код DREAM", sent.Text);
        Assert.Equal(0, factory.Dream.Requests);
    }

    [Fact]
    public async Task Bot_draws_on_the_same_shared_limit_as_the_form()
    {
        using var factory = Bot(s => s.AddSingleton(new SharedLimits(checksPerHour: 1)));
        var client = await factory.ReadyClientAsync();

        await PostAsync(client, Update(Code));
        var first = await factory.Telegram.NextAsync();
        await PostAsync(client, Update("DREAM-UA-010101-ABCDEF12"));
        var second = await factory.Telegram.NextAsync();

        Assert.Contains("доречних полів", first.Text);
        Assert.Contains("Живих перевірок за останню годину забагато", second.Text);
    }

    [Fact]
    public async Task Malformed_update_is_accepted_so_telegram_does_not_resend_it()
    {
        using var factory = Bot();
        var client = await factory.ReadyClientAsync();

        var response = await PostAsync(client, "{not json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Failed_reply_is_logged_without_chat_or_text_and_the_bot_keeps_going()
    {
        var logs = new CapturedLogs();
        using var factory = Bot(s => s.AddSingleton<ILoggerProvider>(logs));
        var client = await factory.ReadyClientAsync();

        factory.Telegram.Fail = HttpStatusCode.Forbidden;
        await PostAsync(client, Update(Code));
        await logs.WaitForAsync("Відповідь бота не надіслано");
        factory.Telegram.Fail = null;
        await PostAsync(client, Update("/help"));
        await factory.Telegram.NextAsync();

        Assert.Contains(logs.Lines, line => line.Contains("Telegram відповів 403", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Code, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Token, StringComparison.Ordinal) || line.Contains(Secret, StringComparison.Ordinal));
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> lines = new();

        public IReadOnlyCollection<string> Lines => lines;

        public ILogger CreateLogger(string categoryName) => new Logger(lines);

        public async Task WaitForAsync(string text)
        {
            for (int i = 0; i < 200; i++)
            {
                if (lines.Any(line => line.Contains(text, StringComparison.Ordinal))) return;
                await Task.Delay(50);
            }
            throw new TimeoutException(text);
        }

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
