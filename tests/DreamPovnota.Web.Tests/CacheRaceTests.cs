using DreamPovnota.Web.Import;
using DreamPovnota.Web.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Гонка кешу: запит прочитав дані за мить до запису знімка, а відповідь поклав у кеш
/// уже після його очищення. Тут «база» це змінна, а запит зупиняється між читанням і відповіддю.
/// </summary>
public sealed class CacheRaceTests : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly HttpClient client;
    private readonly TaskCompletionSource<bool> readDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile string data = "до знімка";
    private int calls;

    public CacheRaceTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<EditionStore>();
        builder.Services.AddOutputCache(CachePolicies.Configure);
        app = builder.Build();
        app.UseOutputCache();
        app.MapGet("/history", async () =>
        {
            string read = data;
            // перший запит тримаємо між читанням «бази» і відповіддю
            if (Interlocked.Increment(ref calls) == 1)
            {
                readDone.SetResult(true);
                await release.Task;
            }
            return read;
        }).CacheOutput(ImportService.CacheTag);
        app.StartAsync().GetAwaiter().GetResult();
        client = app.GetTestClient();
    }

    private async Task<string> ReadBeforeSnapshotAndCacheAfterAsync(Action afterCommit)
    {
        var slow = client.GetStringAsync("/history");
        await readDone.Task;

        data = "після знімка";
        afterCommit();
        await app.Services.GetRequiredService<IOutputCacheStore>().EvictByTagAsync(ImportService.CacheTag, CancellationToken.None);
        release.SetResult(true);
        Assert.Equal("до знімка", await slow);

        return await client.GetStringAsync("/history");
    }

    [Fact]
    public async Task Answer_read_before_the_snapshot_is_not_served_after_it()
    {
        var store = app.Services.GetRequiredService<EditionStore>();

        string next = await ReadBeforeSnapshotAndCacheAfterAsync(store.HistoryChanged);

        Assert.Equal("після знімка", next);
    }

    // без нового покоління очищення за тегом не допомагає: так гонка й виглядала
    [Fact]
    public async Task Eviction_alone_lets_an_answer_read_before_it_back_into_the_cache()
    {
        string next = await ReadBeforeSnapshotAndCacheAfterAsync(() => { });

        Assert.Equal("до знімка", next);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }
}
