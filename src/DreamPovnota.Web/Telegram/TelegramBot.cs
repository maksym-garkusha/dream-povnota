using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace DreamPovnota.Web.Telegram;

/// <summary>
/// Вебхук Telegram-бота. Telegram надсилає оновлення POST-запитом на /telegram/webhook
/// із секретом у заголовку X-Telegram-Bot-Api-Secret-Token (його задає setWebhook).
/// Секрет тільки в заголовку: шлях потрапляє в журнали проксі й історію оболонки, заголовок ні.
/// Відповідаємо одразу 200, а перевірку робить <see cref="BotWorker"/> у фоні: інакше Telegram
/// чекав би на DREAM і після тайм-ауту надсилав би те саме оновлення знову.
/// </summary>
public static class TelegramBot
{
    public const string Path = "/telegram";
    public const string WebhookPath = Path + "/webhook";
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    // загальна межа тіла запиту 16 КБ; оновлення з текстом на 4096 знаків і цитатою буває більшим
    public const int MaxUpdateBytes = 256 * 1024;

    public static IServiceCollection AddTelegramBot(this IServiceCollection services)
    {
        services.AddSingleton(sp => BotSettings.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<BotQueue>();
        services.AddSingleton<ChatLimits>();
        services.AddHostedService<BotWorker>();
        services.AddHostedService<WebhookRegistration>();
        services.AddHttpClient<TelegramClient>(TelegramClient.Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            })
            // стандартний журнал HttpClient пише адресу запиту, а в ній токен
            .RemoveAllLoggers();
        return services;
    }

    /// <summary>Без токена чи секрету адреси вебхука немає зовсім, і на неї відповідає звичайний 404.</summary>
    public static WebApplication MapTelegramBot(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<BotSettings>();
        if (settings.Problem is { } problem) app.Logger.LogWarning("Telegram-бот: {Problem}", problem);
        if (!settings.Enabled) return app;

        app.MapPost(WebhookPath, ReceiveAsync).ExcludeFromDescription();
        app.Logger.LogInformation("Telegram-бот увімкнено");
        return app;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context, BotSettings settings, BotQueue queue, ChatLimits limits, ILoggerFactory logs, CancellationToken ct)
    {
        if (!SameSecret(context.Request.Headers[SecretHeader].ToString(), settings.Secret)) return Results.Unauthorized();

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxUpdateBytes;

        BotJob? job;
        try
        {
            using var update = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            job = Read(update.RootElement);
        }
        catch (JsonException)
        {
            // незрозуміле оновлення приймаємо, щоб Telegram не надсилав його знову і знову
            return Results.Ok();
        }

        if (job is null) return Results.Ok();

        // ліміт чату до черги: інакше один чат пачкою повідомлень зайняв би всю чергу,
        // і повідомлення інших пропадали б
        var (verdict, wait) = limits.Check(job.ChatId);
        if (verdict == ChatVerdict.Drop) return Results.Ok();
        if (verdict == ChatVerdict.Notify) job = job with { Reply = BotReply.Limited(wait) };

        if (!queue.TryAdd(job))
            logs.CreateLogger(typeof(TelegramBot)).LogWarning("Черга бота повна, повідомлення пропущено");
        return Results.Ok();
    }

    /// <summary>
    /// Порівняння за сталий час. Хешуємо обидва рядки, щоб і довжина секрету
    /// не впливала на час порівняння.
    /// </summary>
    public static bool SameSecret(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// <summary>
    /// Повідомлення з приватного чату або null для всього іншого: груп, каналів,
    /// редагувань, натискань кнопок. Повідомлення без тексту (фото, стікер) стає порожнім текстом,
    /// і на нього бот відповідає підказкою формату.
    /// </summary>
    public static BotJob? Read(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object
            || !update.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object)
            return null;
        if (!chat.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "private")
            return null;
        if (!chat.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long chatId))
            return null;

        string text = message.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
        return new BotJob(chatId, text);
    }
}
