using System.Net.Http.Json;
using System.Text.Json;

namespace DreamPovnota.Web.Telegram;

/// <summary>Telegram відповів не 2xx. У повідомленні лише код статусу, без адреси з токеном.</summary>
public sealed class TelegramException(int status) : Exception($"Telegram відповів {status}")
{
    public int Status { get; } = status;
}

/// <summary>
/// Єдиний метод Bot API, який потрібен боту: sendMessage. Токен стоїть у шляху запиту,
/// тому журнал HttpClient для цього клієнта вимкнено, а помилку описуємо тільки кодом статусу.
/// </summary>
public sealed class TelegramClient(HttpClient http, BotSettings settings)
{
    public const string BaseAddress = "https://api.telegram.org/";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static void Configure(HttpClient client)
    {
        client.BaseAddress = new Uri(BaseAddress);
        client.Timeout = RequestTimeout;
        client.MaxResponseContentBufferSize = 64 * 1024;
    }

    /// <summary>Звичайний текст без parse_mode і без прев'ю посилань.</summary>
    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        var message = new SendMessage(chatId, text, new LinkPreviewOptions(IsDisabled: true));
        // косу риску на початку не прибирати: без неї "bot123:..." читається як адреса зі схемою "bot123"
        var path = new Uri($"/bot{settings.Token}/sendMessage", UriKind.Relative);
        using var response = await http.PostAsJsonAsync(path, message, Json, ct);
        if (!response.IsSuccessStatusCode) throw new TelegramException((int)response.StatusCode);
    }

    private sealed record SendMessage(long ChatId, string Text, LinkPreviewOptions LinkPreviewOptions);

    private sealed record LinkPreviewOptions(bool IsDisabled);
}
