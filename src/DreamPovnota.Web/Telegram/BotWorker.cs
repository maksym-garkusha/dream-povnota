using System.Threading.Channels;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Live;

namespace DreamPovnota.Web.Telegram;

/// <summary>Повідомлення з приватного чату: куди відповідати і що написали.</summary>
public sealed record BotJob(long ChatId, string Text);

/// <summary>
/// Черга між вебхуком і перевірками. Обмежена: коли вона повна, нове повідомлення
/// пропускаємо, а не тримаємо запит Telegram відкритим, поки DREAM відповідає.
/// </summary>
public sealed class BotQueue(int capacity = BotQueue.Capacity)
{
    public const int Capacity = 20;

    private readonly Channel<BotJob> channel = Channel.CreateBounded<BotJob>(new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
    });

    /// <summary>false, якщо черга повна.</summary>
    public bool TryAdd(BotJob job) => channel.Writer.TryWrite(job);

    public ChannelReader<BotJob> Reader => channel.Reader;
}

/// <summary>
/// Розбирає повідомлення з черги, перевіряє картку і надсилає відповідь.
/// Двох обробників досить: одночасно в DREAM і так ходять не більше двох живих перевірок.
/// </summary>
public sealed class BotWorker(
    BotQueue queue,
    ChatLimits limits,
    BotSettings settings,
    EditionStore store,
    IServiceScopeFactory scopes,
    ILogger<BotWorker> log) : BackgroundService
{
    public const int Workers = 2;

    protected override Task ExecuteAsync(CancellationToken ct) =>
        settings.Enabled
            ? Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => RunAsync(ct)))
            : Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await HandleAsync(job, ct);
            }
            // збій однієї відповіді не має зупинити обробник; у журнал тільки тип помилки,
            // без тексту повідомлення і chat id
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("Відповідь бота не надіслано: {Error}", ex is TelegramException telegram ? telegram.Message : ex.GetType().Name);
            }
        }
    }

    private async Task HandleAsync(BotJob job, CancellationToken ct)
    {
        var (verdict, wait) = limits.Check(job.ChatId);
        if (verdict == ChatVerdict.Drop) return;

        using var scope = scopes.CreateScope();
        string reply = verdict == ChatVerdict.Notify
            ? BotReply.Limited(wait)
            : await ReplyAsync(BotMessage.Parse(job.Text), scope.ServiceProvider, ct);
        await scope.ServiceProvider.GetRequiredService<TelegramClient>().SendMessageAsync(job.ChatId, reply, ct);
    }

    private async Task<string> ReplyAsync(BotRequest request, IServiceProvider services, CancellationToken ct)
    {
        if (request.Intent == BotIntent.Help) return BotReply.Help(settings.Site);
        if (request.Intent == BotIntent.Invalid || request.Code is null) return BotReply.Invalid(request.Error ?? CodeInput.Example);

        // спільний кошик живих перевірок витрачає сам LiveCheck, і тільки перед запитом у DREAM
        var outcome = await services.GetRequiredService<LiveCheck>().RunAsync(request.Code, ct);
        bool organisationOnSite = outcome.Result?.Edrpou is { } edrpou
                                  && store.Current?.OrganisationsByKey.ContainsKey(edrpou) == true;
        return BotReply.Check(request.Code, outcome, settings.Site, organisationOnSite);
    }
}
