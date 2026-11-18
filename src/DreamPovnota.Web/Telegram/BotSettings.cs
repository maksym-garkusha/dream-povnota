using System.Text.RegularExpressions;

namespace DreamPovnota.Web.Telegram;

/// <summary>
/// Налаштування бота зі змінних середовища. Без токена бот вимкнений повністю:
/// адреси вебхука немає, фонова черга нічого не робить. Без секрету теж вимкнений,
/// бо тоді не відрізнити запит від Telegram від чужого.
/// </summary>
public sealed partial class BotSettings
{
    public const string TokenName = "TELEGRAM_BOT_TOKEN";
    public const string SecretName = "TELEGRAM_WEBHOOK_SECRET";
    public const string SiteName = "PUBLIC_URL";

    public static readonly Uri DefaultSite = new("https://dream-povnota.onrender.com/");

    private BotSettings(string token, string secret, Uri site, string? problem)
    {
        Token = token;
        Secret = secret;
        Site = site;
        Problem = problem;
    }

    public string Token { get; }

    public string Secret { get; }

    /// <summary>Адреса сервісу для посилань у відповідях.</summary>
    public Uri Site { get; }

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
        string secret = config[SecretName]?.Trim() ?? "";
        Uri site = Uri.TryCreate(config[SiteName], UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? url
            : DefaultSite;

        if (token.Length == 0) return new BotSettings("", "", site, null);
        if (!TokenPattern().IsMatch(token))
            return new BotSettings("", "", site, $"{TokenName} не схожий на токен бота, бот вимкнено");
        if (!SecretPattern().IsMatch(secret))
            return new BotSettings("", "", site, $"{SecretName} порожній або не з 16-256 знаків A-Z, a-z, 0-9, _ і -, бот вимкнено");
        return new BotSettings(token, secret, site, null);
    }
}
