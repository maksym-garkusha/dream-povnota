using System.Net;
using System.Text;
using DreamPovnota.Digest;

namespace DreamPovnota.Tests;

public class DigestHistoryClientTests
{
    /// <summary>Одна відповідь на будь-який запит; адресу запиту запам'ятовує.</summary>
    private sealed class OneAnswer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Reads_the_baseline_as_the_service_writes_it()
    {
        // так відповідає /api/v1/digest/baseline: camelCase, дата оновлення без часу
        var handler = new OneAnswer(HttpStatusCode.OK, """
            {"historyFrom":"2026-11-18T01:12:54.538866+00:00","taken":"2026-11-18T01:12:54.538866+00:00",
             "checks":["description","location"],
             "cards":[{"code":"050923-8119373D","title":"Капітальний ремонт мостового переходу","edrpou":"05422303",
                       "stage":"impl","updated":"2025-01-23","applicable":3,"filled":1}]}
            """);

        var baseline = await new DigestHistoryClient(new HttpClient(handler))
            .FetchAsync("https://dream-povnota.example/", new DateOnly(2026, 11, 12), CancellationToken.None);

        Assert.Equal("https://dream-povnota.example/api/v1/digest/baseline?from=2026-11-12", handler.Asked!.ToString());
        Assert.Equal(["description", "location"], baseline.Checks);
        var card = Assert.Single(baseline.Cards);
        Assert.Equal(new DateOnly(2025, 1, 23), card.Updated);
        Assert.Equal(("05422303", 3, 1), (card.Edrpou, card.Applicable, card.Filled));
    }

    [Fact]
    public async Task Service_without_history_answers_with_an_error_not_an_empty_baseline()
    {
        var handler = new OneAnswer(HttpStatusCode.ServiceUnavailable, """{"title":"Історія тимчасово недоступна"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() => new DigestHistoryClient(new HttpClient(handler))
            .FetchAsync("https://dream-povnota.example", new DateOnly(2026, 11, 12), CancellationToken.None));
    }
}
