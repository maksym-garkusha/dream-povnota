using System.Net;
using DreamPovnota.Dream;

namespace DreamPovnota.Tests;

public class DreamClientTests
{
    [Fact]
    public async Task Feed_turns_pages_with_the_date_of_the_last_entry()
    {
        var api = new FakeDream()
            .Json("ideas", Fixture.Text("feed-page-1.json"))
            .Json("ideas?from=2025-01-16T16%3A39%3A37.770396Z", Fixture.Text("feed-page-2.json"));

        var entries = await api.Client(pageSize: 3).ProjectFeedAsync().ToListAsync();

        Assert.Equal(5, entries.Count);
        Assert.Equal("u7l14aez-a34x-45ls-ai13-8992j35a5q1p", entries[^1].Id);
        Assert.Equal(DateTimeOffset.Parse("2025-01-16T16:39:38.641666Z"), entries[^1].Updated);
    }

    [Fact]
    public async Task Short_page_ends_the_feed()
    {
        var api = new FakeDream().Json("objects", Fixture.Text("feed-page-1.json"));

        var entries = await api.Client(pageSize: 1000).ObjectFeedAsync().ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal(["objects"], api.Requests);
    }

    [Fact]
    public async Task Entry_without_id_does_not_end_the_feed_early()
    {
        var api = new FakeDream()
            .Json("ideas", """{"data":[{"internal":{"id":"a"},"external":{"updated":"2025-01-01T00:00:00Z"}},{"internal":{},"external":{"updated":"2025-01-02T00:00:00Z"}}]}""")
            .Feed("ideas?from=2025-01-02T00%3A00%3A00Z", ("b", "2025-01-03T00:00:00Z"));

        var entries = await api.Client(pageSize: 2).ProjectFeedAsync().ToListAsync();

        Assert.Equal(["a", "b"], entries.Select(e => e.Id));
    }

    [Fact]
    public async Task Missing_card_is_null_not_an_error()
    {
        var api = new FakeDream();

        Assert.Null(await api.Client().GetProjectAsync("gone"));
    }

    [Fact]
    public async Task Server_error_is_retried()
    {
        var api = new FakeDream()
            .Status("ideas/a", HttpStatusCode.BadGateway, once: true)
            .Status("ideas/a", HttpStatusCode.TooManyRequests, once: true)
            .Json("ideas/a", "{}");

        Assert.Equal("{}", await api.Client().GetProjectAsync("a"));
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Broken_connection_is_retried()
    {
        var api = new FakeDream().Broken("objects/a", once: true).Json("objects/a", "{}");

        Assert.Equal("{}", await api.Client().GetObjectAsync("a"));
    }

    [Fact]
    public async Task Gives_up_after_four_attempts()
    {
        var api = new FakeDream().Status("ideas/a", HttpStatusCode.ServiceUnavailable);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.Client().GetProjectAsync("a"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(DreamClient.MaxAttempts, api.Requests.Count);
    }

    [Fact]
    public async Task Client_error_is_not_retried()
    {
        var api = new FakeDream().Status("ideas/a", HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<HttpRequestException>(() => api.Client().GetProjectAsync("a"));
        Assert.Single(api.Requests);
    }
}
