using System.Net;
using DreamPovnota.Cards;
using DreamPovnota.Store;
using DreamPovnota.Sync;

namespace DreamPovnota.Tests;

public sealed class CardSyncTests : IDisposable
{
    private const string Day1 = "2025-01-01T10:00:00.000001Z";
    private const string Day2 = "2025-01-02T10:00:00.000001Z";

    private readonly TempDir dir = new();
    private CardStore Store => new(dir.Path);

    public void Dispose() => dir.Dispose();

    // p1 у реалізації, p2 скасований, p3 ідея; кожен зі своїм об'єктом
    private static FakeDream Api(string p1Updated = Day1) => new FakeDream()
        .Feed("ideas", ("p1", p1Updated), ("p2", Day1), ("p3", Day1))
        .Json("ideas/p1", FakeDream.Card("project-active.json", "p1", p1Updated, "o1"))
        .Json("ideas/p2", FakeDream.Card("project-cancelled.json", "p2", Day1, "o2"))
        .Json("ideas/p3", FakeDream.Card("project-idea.json", "p3", Day1, "o3"))
        .Feed("objects", ("o1", Day1), ("o2", Day1), ("o3", Day1), ("o4", Day1))
        .Json("objects/o1", FakeDream.Card("object-active.json", "o1", Day1))
        .Json("objects/o2", FakeDream.Card("object-defined.json", "o2", Day1))
        .Json("objects/o3", FakeDream.Card("object-idea.json", "o3", Day1));

    private Task<SyncResult> Run(FakeDream api, int? limit = null) =>
        new CardSync(api.Client(), Store).RunAsync(new SyncOptions(Parallel: 2, Limit: limit));

    [Fact]
    public async Task First_run_downloads_every_card_into_the_cache()
    {
        var result = await Run(Api());

        Assert.Equal(new SyncCounts(3, 3, 0, 0, 0, 0, 0), result.Projects);
        Assert.Equal(["p1", "p2", "p3"], (await Store.ReadProjectsAsync()).Cards.Select(p => p.Id).Order());
    }

    [Fact]
    public async Task Objects_are_loaded_only_for_open_projects()
    {
        var api = Api();

        var result = await Run(api);

        Assert.Equal(2, result.Objects.Downloaded);
        Assert.DoesNotContain("objects/o2", api.Requests);
        Assert.DoesNotContain("objects/o4", api.Requests);
        Assert.Equal(["o1", "o3"], (await Store.ReadObjectsAsync()).Cards.Select(o => o.Id).Order());
    }

    [Fact]
    public async Task Second_run_takes_unchanged_cards_from_the_cache()
    {
        await Run(Api());
        var api = Api();

        var result = await Run(api);

        Assert.Equal(new SyncCounts(3, 0, 3, 0, 0, 0, 0), result.Projects);
        Assert.Equal(new SyncCounts(2, 0, 2, 0, 0, 0, 0), result.Objects);
        Assert.Equal(["ideas", "objects"], api.Requests);
    }

    [Fact]
    public async Task Changed_card_is_downloaded_again()
    {
        await Run(Api());
        var api = Api(p1Updated: Day2);

        var result = await Run(api);

        Assert.Equal(1, result.Projects.Downloaded);
        Assert.Equal(2, result.Projects.FromCache);
        Assert.Contains("ideas/p1", api.Requests);
        var p1 = (await Store.ReadProjectsAsync()).Cards.Single(p => p.Id == "p1");
        Assert.Equal(DateTimeOffset.Parse(Day2), p1.Updated);
    }

    [Fact]
    public async Task Card_that_left_the_feed_is_removed_from_the_cache()
    {
        await Run(Api());
        var api = Api().Feed("ideas", ("p1", Day1), ("p3", Day1));

        var result = await Run(api);

        Assert.Equal(1, result.Projects.Removed);
        Assert.DoesNotContain((await Store.ReadProjectsAsync()).Cards, p => p.Id == "p2");
    }

    [Fact]
    public async Task Card_answering_404_is_counted_as_gone_not_as_error()
    {
        var api = Api().Status("ideas/p3", HttpStatusCode.NotFound);

        var result = await Run(api);

        Assert.Equal(1, result.Projects.Gone);
        Assert.Equal(0, result.Projects.Errors);
        Assert.Equal(2, (await Store.ReadProjectsAsync()).Cards.Count);
    }

    [Fact]
    public async Task Card_is_downloaded_after_a_server_error()
    {
        var api = Api().Status("ideas/p1", HttpStatusCode.InternalServerError, once: true);

        var result = await Run(api);

        Assert.Equal(3, result.Projects.Downloaded);
        Assert.Equal(2, api.Requests.Count(r => r == "ideas/p1"));
    }

    [Fact]
    public async Task Failed_download_keeps_the_cached_version()
    {
        await Run(Api());
        var api = Api(p1Updated: Day2).Status("ideas/p1", HttpStatusCode.BadGateway);

        var result = await Run(api);

        Assert.Equal(1, result.Projects.Failed);
        var p1 = (await Store.ReadProjectsAsync()).Cards.Single(p => p.Id == "p1");
        Assert.Equal(DateTimeOffset.Parse(Day1), p1.Updated);
    }

    [Fact]
    public async Task Card_that_cannot_be_parsed_is_counted_with_its_id()
    {
        var api = Api().Json("ideas/p3", "{\"internal\":{\"id\":\"p3\"}}");

        var result = await Run(api);

        Assert.Equal(1, result.Projects.Unparsed);
        Assert.Equal(["p3"], result.UnparsedIds);
    }

    [Fact]
    public async Task One_error_in_five_cards_is_too_many()
    {
        // 3 проєкти і 2 об'єкти, одна помилка це 20%
        var api = Api().Status("ideas/p2", HttpStatusCode.ServiceUnavailable);

        var result = await Run(api);

        Assert.True(result.TooManyErrors);
    }

    [Fact]
    public void Two_percent_of_errors_is_still_acceptable()
    {
        var projects = new SyncCounts(100, 98, 0, 0, 2, 0, 0);
        var objects = new SyncCounts(0, 0, 0, 0, 0, 0, 0);

        Assert.False(new SyncResult(projects, objects, 0, []).TooManyErrors);
        Assert.True(new SyncResult(projects with { Unparsed = 1 }, objects, 0, []).TooManyErrors);
    }

    [Fact]
    public async Task Limit_takes_only_the_first_projects_of_the_feed()
    {
        var api = Api();

        var result = await Run(api, limit: 2);

        Assert.Equal(2, result.Projects.InFeed);
        Assert.DoesNotContain("ideas/p3", api.Requests);
    }

    [Fact]
    public async Task Object_missing_from_the_object_feed_is_counted_and_not_requested()
    {
        var api = Api().Feed("objects", ("o3", Day1));

        var result = await Run(api);

        Assert.Equal(1, result.ObjectsNotInFeed);
        Assert.DoesNotContain("objects/o1", api.Requests);
    }

    [Fact]
    public async Task Cached_project_keeps_its_parsed_fields()
    {
        await Run(Api());

        var p1 = (await Store.ReadProjectsAsync()).Cards.Single(p => p.Id == "p1");

        Assert.Equal(Stage.Implementation, p1.Stage);
        Assert.Equal(3_735_998m, p1.CostEstimate);
        Assert.Equal(["o1"], p1.ObjectIds);
    }
}
