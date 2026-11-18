using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace DreamPovnota.Web.Tests;

/// <param name="Url">Адреса запиту без параметрів, разом з bot{токен}.</param>
/// <param name="Body">Тіло sendMessage як є.</param>
public sealed record SentMessage(string Url, JsonElement Body)
{
    public long ChatId => Body.GetProperty("chat_id").GetInt64();

    public string Text => Body.GetProperty("text").GetString()!;
}

/// <summary>Підставний Bot API: запам'ятовує надіслані повідомлення і відповідає 200 або заданим статусом.</summary>
public sealed class FakeTelegram : HttpMessageHandler
{
    private readonly Channel<SentMessage> sent = Channel.CreateUnbounded<SentMessage>();

    /// <summary>Чим відповідати на наступні запити; null означає 200.</summary>
    public HttpStatusCode? Fail { get; set; }

    public async Task<SentMessage> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await sent.Reader.ReadAsync(timeout.Token);
    }

    public bool TryNext(out SentMessage? message) => sent.Reader.TryRead(out message);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Fail is { } status) return new HttpResponseMessage(status);
        string json = await request.Content!.ReadAsStringAsync(ct);
        await sent.Writer.WriteAsync(new SentMessage(request.RequestUri!.GetLeftPart(UriPartial.Path), JsonDocument.Parse(json).RootElement.Clone()), ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":{}}") };
    }
}
