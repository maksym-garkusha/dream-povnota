using System.Net;
using System.Xml.Linq;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DreamPovnota.Web.Tests;

public class ImportServiceTests
{
    /// <summary>Перші <c>failures</c> запитів обриваються посеред тіла, далі віддається data.json з фікстур.</summary>
    private sealed class FlakyPages(int failures) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref requests) <= failures)
                throw new HttpRequestException("Error while copying content to a stream.", new IOException("The response ended prematurely."));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Fixtures.EditionPath, ct)) };
        }
    }

    private sealed class NoOutputCache : IOutputCacheStore
    {
        public ValueTask EvictByTagAsync(string tag, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken ct) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static bool Tick(FakeTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromSeconds(1));
        return true;
    }

    [Fact]
    public async Task Edition_loads_after_two_failed_attempts_within_seconds_not_an_hour()
    {
        var pages = new FlakyPages(failures: 2);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-18T01:17:00Z"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("DATA_URL", "https://pages.example/data.json")]).Build();
        var store = new EditionStore();
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        using var service = new ImportService(
            new EditionSource(new HttpClient(pages), configuration),
            store,
            TestDatabase.Open(""),
            new DbKeyRepository(TestDatabase.Open(""), NullLogger<DbKeyRepository>.Instance),
            exports,
            new NoOutputCache(),
            clock,
            NullLogger<ImportService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // лічильник запитів росте ще до збою, тож чекаємо саме на записану причину
        await WaitFor(() => store.Status.LastError is not null);
        Assert.Equal(1, pages.Requests);
        Assert.Null(store.Current);
        Assert.Equal("HttpRequestException → IOException", store.Status.LastError);

        // годинник крутимо по секунді: так не важливо, чи сервіс уже встиг почати чекати
        var failedAt = clock.GetUtcNow();
        await WaitFor(() => Tick(clock) && pages.Requests == 2);
        await WaitFor(() => Tick(clock) && store.Current is not null);

        Assert.Equal(3, pages.Requests);
        Assert.InRange(clock.GetUtcNow() - failedAt, ImportService.RetryDelays[0] + ImportService.RetryDelays[1], ImportService.Interval / 2);
        Assert.Null(store.Status.LastError);
        await service.StopAsync(CancellationToken.None);
    }

    [PostgresFact]
    public async Task New_edition_and_its_snapshot_each_move_the_cache_generation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("DATA_URL", "https://pages.example/data.json")]).Build();
        var store = new EditionStore();
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        using var service = new ImportService(
            new EditionSource(new HttpClient(new FlakyPages(failures: 0)), configuration),
            store,
            database.Db,
            new DbKeyRepository(database.Db, NullLogger<DbKeyRepository>.Instance),
            exports,
            new NoOutputCache(),
            TimeProvider.System,
            NullLogger<ImportService>.Instance);

        Assert.True(await service.ImportOnceAsync(CancellationToken.None));

        Assert.Equal(1L, await database.ScalarAsync<long>("select count(*) from snapshot"));
        Assert.Equal(2, store.Generation);
    }

    [PostgresFact]
    public async Task Form_key_made_while_the_database_was_down_is_saved_on_the_next_import()
    {
        await using var database = await TestDatabase.CreateAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("DATA_URL", "https://pages.example/data.json")]).Build();
        var keys = new DbKeyRepository(database.Db, NullLogger<DbKeyRepository>.Instance);
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        using var service = new ImportService(
            new EditionSource(new HttpClient(new FlakyPages(failures: 0)), configuration),
            new EditionStore(),
            database.Db,
            keys,
            exports,
            new NoOutputCache(),
            TimeProvider.System,
            NullLogger<ImportService>.Instance);
        Assert.True(await service.ImportOnceAsync(CancellationToken.None));

        // база вже змігрована і знімок записано, а потім вона на хвилину зникає
        database.Db.MarkDown();
        keys.StoreElement(new XElement("key", new XAttribute("id", "k2")), "key-k2");
        Assert.True(keys.HasUnsaved);
        database.Db.MarkUp();
        await service.ImportOnceAsync(CancellationToken.None);

        Assert.False(keys.HasUnsaved);
        Assert.Equal(1, await database.ScalarAsync<long>("select count(*) from data_protection_key where name = 'key-k2'"));
    }

    [Theory]
    [InlineData(0, 3600)]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(5, 300)]
    [InlineData(6, 600)]
    [InlineData(40, 600)]
    public void Retry_delay_grows_and_returns_to_hourly_after_success(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ImportService.DelayAfter(failures));
    }
}
