using System.Net;
using System.Text;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Підставний API DREAM для живої перевірки: відповідь за шляхом після базової адреси
/// ("ideas/190424-A10FC799", "objects/..."). Невідомий шлях відповідає 404.
/// </summary>
public sealed class FakeDream : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> routes = new(StringComparer.Ordinal);
    private int requests;

    /// <summary>Відповідь на будь-який /objects/{id}, якщо окремої немає.</summary>
    public string? AnyObject { get; set; }

    public int Requests => Volatile.Read(ref requests);

    public FakeDream Json(string path, string json)
    {
        routes[path] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return this;
    }

    public FakeDream Status(string path, HttpStatusCode status)
    {
        routes[path] = () => new HttpResponseMessage(status);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref requests);
        string path = request.RequestUri!.AbsolutePath[request.RequestUri.AbsolutePath.IndexOf("/dream/", StringComparison.Ordinal)..]["/dream/".Length..];
        if (routes.TryGetValue(path, out var respond)) return Task.FromResult(respond());
        if (AnyObject is not null && path.StartsWith("objects/", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnyObject, Encoding.UTF8, "application/json") });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
