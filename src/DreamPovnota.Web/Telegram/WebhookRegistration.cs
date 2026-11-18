using System.Net.Sockets;

namespace DreamPovnota.Web.Telegram;

/// <summary>
/// Після старту сам каже Telegram, куди надсилати оновлення (setWebhook), щоб для
/// увімкнення бота вистачало токена в налаштуваннях. Працює у фоні й старт не тримає.
/// Повторює тільки те, що може минути: збій мережі, 429 і 5xx; на інший 4xx (поганий токен)
/// повтор не допоможе.
/// </summary>
public sealed class WebhookRegistration(BotSettings settings, IServiceScopeFactory scopes, ILogger<WebhookRegistration> log) : BackgroundService
{
    /// <summary>Паузи перед повторами; спроб на одну більше.</summary>
    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    protected override Task ExecuteAsync(CancellationToken ct) => RegisterAsync(ct);

    /// <summary>true, якщо Telegram прийняв адресу.</summary>
    public async Task<bool> RegisterAsync(CancellationToken ct)
    {
        if (!settings.Enabled) return false;
        if (settings.WebhookUrl is not { } url)
        {
            log.LogWarning("Вебхук Telegram не зареєстровано: немає {Url} чи {RenderUrl}", BotSettings.WebhookUrlName, BotSettings.RenderUrlName);
            return false;
        }

        for (int attempt = 0; ; attempt++)
        {
            string error;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TelegramClient>().SetWebhookAsync(url, ct);
                log.LogInformation("Вебхук Telegram зареєстровано на {Path}", url.AbsolutePath);
                return true;
            }
            catch (TelegramException ex) when (ex.Status is >= 400 and < 500 and not 429)
            {
                log.LogWarning("Вебхук Telegram не зареєстровано: {Error}", ex.Message);
                return false;
            }
            catch (TelegramException ex)
            {
                error = ex.Message;
            }
            // тип помилки без тексту: у тексті HttpClient може бути адреса з токеном
            catch (Exception ex) when (ex is HttpRequestException or SocketException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                error = ex.GetType().Name;
            }

            if (attempt >= Delays.Count)
            {
                log.LogWarning("Вебхук Telegram не зареєстровано після {Attempts} спроб: {Error}", attempt + 1, error);
                return false;
            }
            await Task.Delay(Delays[attempt], ct);
        }
    }
}
