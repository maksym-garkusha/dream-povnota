using System.Net;
using System.Text.Json.Nodes;
using DreamPovnota.Cards;
using DreamPovnota.Web.Live;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamPovnota.Web.Tests;

public class LiveCheckTests
{
    private static readonly string Active = Fixtures.Text("project-active.json");
    private static readonly string Code = CardParser.ParseProject(Active).Code["DREAM-UA-".Length..];

    private static LiveCheck Check(FakeDream dream, IMemoryCache? cache = null)
    {
        var http = new HttpClient(dream);
        LiveCheck.Configure(http);
        return new LiveCheck(http, cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }), TimeProvider.System, NullLogger<LiveCheck>.Instance);
    }

    [Fact]
    public async Task Open_card_is_checked_with_its_objects()
    {
        var dream = new FakeDream { AnyObject = Fixtures.Text("object-active.json") }.Json("ideas/" + Code, Active);

        var outcome = await Check(dream).RunAsync(Code, CancellationToken.None);

        Assert.Equal(LiveStatus.Found, outcome.Status);
        Assert.True(outcome.Result!.IsOpen);
        Assert.Equal(0, outcome.Result.MissingObjects);
        Assert.Equal(outcome.Result.ScoreApplicable, outcome.Result.Filled.Count(c => c.CountsInScore) + outcome.Result.Missing.Count(c => c.CountsInScore));
    }

    [Fact]
    public async Task Closed_card_is_reported_as_closed()
    {
        string cancelled = Fixtures.Text("project-cancelled.json");
        string code = CardParser.ParseProject(cancelled).Code["DREAM-UA-".Length..];
        var dream = new FakeDream().Json("ideas/" + code, cancelled);

        var outcome = await Check(dream).RunAsync(code, CancellationToken.None);

        Assert.False(outcome.Result!.IsOpen);
    }

    [Fact]
    public async Task Missing_card_is_not_found_and_remembered()
    {
        var dream = new FakeDream();
        var check = Check(dream);

        var first = await check.RunAsync("010101-ABCDEF12", CancellationToken.None);
        var second = await check.RunAsync("010101-ABCDEF12", CancellationToken.None);

        Assert.Equal(LiveStatus.NotFound, first.Status);
        Assert.Equal(LiveStatus.NotFound, second.Status);
        Assert.Equal(1, dream.Requests);
    }

    [Fact]
    public async Task Card_with_more_than_thirty_objects_is_not_fetched_further()
    {
        var json = JsonNode.Parse(Active)!;
        var processes = json["cdu_response"]!["relatedProcesses"]!.AsArray();
        for (int i = 0; i < LiveCheck.MaxObjects + 1; i++)
            processes.Add(new JsonObject
            {
                ["id"] = $"p{i}",
                ["scheme"] = "UA-DREAM",
                ["identifier"] = $"{i:D8}-0000-0000-0000-000000000000",
                ["relationship"] = "object",
            });
        var dream = new FakeDream { AnyObject = Fixtures.Text("object-active.json") }.Json("ideas/" + Code, json.ToJsonString());

        var outcome = await Check(dream).RunAsync(Code, CancellationToken.None);

        Assert.Equal(LiveStatus.TooManyObjects, outcome.Status);
        Assert.Equal(1, dream.Requests);
    }

    [Fact]
    public async Task Response_larger_than_the_cap_makes_dream_unavailable()
    {
        string huge = "{\"x\":\"" + new string('a', LiveCheck.MaxResponseBytes + 10) + "\"}";
        var dream = new FakeDream().Json("ideas/" + Code, huge);

        var outcome = await Check(dream).RunAsync(Code, CancellationToken.None);

        Assert.Equal(LiveStatus.Unavailable, outcome.Status);
    }

    [Fact]
    public async Task Server_error_makes_dream_unavailable_after_two_attempts_and_is_not_cached()
    {
        var dream = new FakeDream().Status("ideas/" + Code, HttpStatusCode.InternalServerError);
        var check = Check(dream);

        var outcome = await check.RunAsync(Code, CancellationToken.None);

        Assert.Equal(LiveStatus.Unavailable, outcome.Status);
        Assert.Equal(2, dream.Requests);
        Assert.Null(check.Cached(Code));
    }

    [Fact]
    public async Task Answer_for_a_different_card_is_refused()
    {
        var dream = new FakeDream().Json("ideas/010101-ABCDEF12", Active);

        var outcome = await Check(dream).RunAsync("010101-ABCDEF12", CancellationToken.None);

        Assert.Equal(LiveStatus.Unavailable, outcome.Status);
    }
}
