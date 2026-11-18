using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace DreamPovnota.Web.Telegram;

public enum ChatVerdict
{
    Allowed,

    /// <summary>Ліміт вичерпано; один раз сказати про це.</summary>
    Notify,

    /// <summary>Ліміт вичерпано і про це вже сказано; мовчати.</summary>
    Drop,
}

/// <summary>
/// Скільки повідомлень за хвилину і за годину бот відповідає одному чату.
/// Лічильники тримаються тільки в пам'яті і ключ у них не chat id, а його відбиток
/// HMAC з випадковим ключем. Ключ зникає з перезапуском, тож відбиток ні з чим не зіставити.
/// Про перевищення бот пише раз на <see cref="NoticeEvery"/>, а не на кожне повідомлення.
/// </summary>
public sealed class ChatLimits : IDisposable
{
    public const int PerMinute = 6;
    public const int PerHour = 30;
    public static readonly TimeSpan NoticeEvery = TimeSpan.FromMinutes(10);

    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly PartitionedRateLimiter<string> messages;
    private readonly PartitionedRateLimiter<string> notices;

    public ChatLimits(int perMinute = PerMinute, int perHour = PerHour)
    {
        messages = PartitionedRateLimiter.CreateChained(
            Window(perMinute, TimeSpan.FromMinutes(1)),
            Window(perHour, TimeSpan.FromHours(1)));
        notices = Window(1, NoticeEvery);
    }

    public (ChatVerdict Verdict, TimeSpan RetryAfter) Check(long chatId)
    {
        string chat = Fingerprint(chatId);
        using var lease = messages.AttemptAcquire(chat);
        if (lease.IsAcquired) return (ChatVerdict.Allowed, TimeSpan.Zero);

        var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : TimeSpan.FromMinutes(1);
        using var notice = notices.AttemptAcquire(chat);
        return (notice.IsAcquired ? ChatVerdict.Notify : ChatVerdict.Drop, wait);
    }

    private string Fingerprint(long chatId)
    {
        byte[] hash = HMACSHA256.HashData(key, BitConverter.GetBytes(chatId));
        return Convert.ToHexString(hash, 0, 16);
    }

    private static PartitionedRateLimiter<string> Window(int limit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(chat => RateLimitPartition.GetFixedWindowLimiter(chat, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = window,
            QueueLimit = 0,
        }));

    public void Dispose()
    {
        messages.Dispose();
        notices.Dispose();
    }
}
