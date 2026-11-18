using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamPovnota.Web.Telegram;

/// <summary>
/// Налаштування бота зі змінних середовища. Без токена бот вимкнений повністю:
/// адреси вебхука немає, фонова черга нічого не робить. Секрет вебхука можна задати
/// окремо, а без нього він виводиться з токена (<see cref="DeriveSecret"/>).
/// </summary>
public sealed partial class BotSettings
{
    public const string TokenName = "TELEGRAM_BOT_TOKEN";
    public const string SecretName = "TELEGRAM_WEBHOOK_SECRET";
    public const string WebhookUrlName = "TELEGRAM_WEBHOOK_URL";

    /// <summary>Публічну адресу сервісу Render задає сам.</summary>
    public const string RenderUrlName = "RENDER_EXTERNAL_URL";

    public const string SiteName = "PUBLIC_URL";

    public static readonly Uri DefaultSite = new("https://dream-povnota.onrender.com/");

    private BotSettings(string token, string secret, Uri site, Uri? webhookUrl, string? problem)
    {
        Token = token;
        Secret = secret;
        Site = site;
        WebhookUrl = webhookUrl;
        Problem = problem;
    }

    public string Token { get; }

    public string Secret { get; }

    /// <summary>Адреса сервісу для посилань у відповідях.</summary>
    public Uri Site { get; }

    /// <summary>Куди Telegram має надсилати оновлення; null, якщо публічної адреси не задано.</summary>
    public Uri? WebhookUrl { get; }

    public bool Enabled => Token.Length > 0 && Secret.Length > 0;

    /// <summary>Чому бот вимкнений, якщо токен задано. Самих значень тут немає.</summary>
    public string? Problem { get; }

    // формат токена від @BotFather: число, двокрапка, рядок; інший токен у шлях запиту не підставляємо
    [GeneratedRegex("^[0-9]{1,20}:[A-Za-z0-9_-]{20,100}$")]
    private static partial Regex TokenPattern();

    // Telegram дозволяє в secret_token лише ці знаки і до 256 штук; коротший за 16 вважаємо слабким
    [GeneratedRegex("^[A-Za-z0-9_-]{16,256}$")]
    private static partial Regex SecretPattern();

    public static BotSettings From(IConfiguration config)
    {
        string token = config[TokenName]?.Trim() ?? "";
        string? secret = config[SecretName]?.Trim();
        Uri site = HttpsUrl(config[SiteName]) ?? DefaultSite;
        Uri? webhookUrl = (HttpsUrl(config[WebhookUrlName]) ?? HttpsUrl(config[RenderUrlName])) is { } root
            ? new Uri(root, TelegramBot.WebhookPath)
            : null;

        if (token.Length == 0) return Off(site, null);
        if (!TokenPattern().IsMatch(token))
            return Off(site, $"{TokenName} не схожий на токен бота, бот вимкнено");
        if (string.IsNullOrEmpty(secret)) return new BotSettings(token, DeriveSecret(token), site, webhookUrl, null);
        if (!SecretPattern().IsMatch(secret))
            return Off(site, $"{SecretName} не з 16-256 знаків A-Z, a-z, 0-9, _ і -, бот вимкнено");
        return new BotSettings(token, secret, site, webhookUrl, null);
    }

    /// <summary>
    /// Секрет вебхука з токена: HMAC-SHA256 з токеном як ключем, 64 знаки hex.
    /// Окремої змінної тоді не треба, і секрет не міняється між запусками. Хто знає токен,
    /// і так може переналаштувати вебхук, тож виведений секрет нічого не послаблює.
    /// </summary>
    public static string DeriveSecret(string token) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), "dream-povnota/telegram-webhook"u8));

    private static BotSettings Off(Uri site, string? problem) => new("", "", site, null, problem);

    private static Uri? HttpsUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
}
