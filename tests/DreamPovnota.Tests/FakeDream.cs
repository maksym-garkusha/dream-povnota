using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DreamPovnota.Dream;

namespace DreamPovnota.Tests;

/// <summary>
/// Підставний API DREAM. Відповіді задаються за шляхом із запитом
/// ("ideas", "ideas?from=...", "ideas/{id}"); кілька відповідей на один шлях
/// віддаються по черзі, остання повторюється. Невідомий шлях відповідає 404.
/// </summary>
public sealed class FakeDream : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> routes = new();
    private readonly List<string> requests = new();

    public IReadOnlyList<string> Requests
    {
        get { lock (requests) return requests.ToList(); }
    }

    public DreamClient Client(int pageSize = 1000) =>
        new(DreamClient.CreateHttpClient(this)) { PageSize = pageSize, FirstRetryDelay = TimeSpan.Zero };

    public FakeDream Json(string path, string json) =>
        Add(path, () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public FakeDream Status(string path, HttpStatusCode status) =>
        Add(path, () => new HttpResponseMessage(status));

    public FakeDream Broken(string path) =>
        Add(path, () => throw new HttpRequestException("з'єднання обірвано"));

    /// <summary>Стрічка з одної сторінки.</summary>
    public FakeDream Feed(string path, params (string Id, string Updated)[] entries) =>
        Json(path, FeedJson(entries));

    public static string FeedJson(params (string Id, string Updated)[] entries) =>
        new JsonObject
        {
            ["data"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject
            {
                ["internal"] = new JsonObject { ["id"] = e.Id, ["code"] = "000000-" + e.Id },
                ["external"] = new JsonObject { ["updated"] = e.Updated },
            }).ToArray()),
        }.ToJsonString();

    /// <summary>Картка з фікстури з підміненими id і датою зміни.</summary>
    public static string Card(string fixture, string id, string updated, params string[] objectIds)
    {
        var node = JsonNode.Parse(Fixture.Text(fixture))!;
        node["internal"]!["id"] = id;
        node["cdu_response"]!["updated"] = updated;
        if (objectIds.Length > 0)
        {
            node["cdu_response"]!["relatedProcesses"] = new JsonArray(objectIds.Select(o => (JsonNode)new JsonObject
            {
                ["scheme"] = "UA-DREAM",
                ["identifier"] = o,
                ["relationship"] = "object",
            }).ToArray());
        }
        return node.ToJsonString();
    }

    private FakeDream Add(string path, Func<HttpResponseMessage> response)
    {
        if (!routes.TryGetValue(path, out var queue)) routes[path] = queue = new();
        queue.Enqueue(response);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string path = request.RequestUri!.PathAndQuery["/marketplace/public/dream/".Length..];
        lock (requests) requests.Add(path);

        Func<HttpResponseMessage> next;
        lock (routes)
        {
            if (!routes.TryGetValue(path, out var queue))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }
        return Task.FromResult(next());
    }
}
