using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    /// <summary>Pages з ETag: той самий файл удруге дає 304, як справжні.</summary>
    private sealed class Pages : HttpMessageHandler
    {
        public byte[] Body { get; set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var tag = new EntityTagHeaderValue("\"" + Convert.ToHexStringLower(SHA256.HashData(Body)) + "\"");
            if (request.Headers.IfNoneMatch.Contains(tag)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) };
            response.Headers.ETag = tag;
            return Task.FromResult(response);
        }
    }

    private static readonly string DigestEditionPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "digest-edition.json");

    /// <summary>Той самий data.json на день пізніше, у якому з усіх 26 карток зникли заповнені поля.</summary>
    private static byte[] Emptied(byte[] body)
    {
        var root = JsonNode.Parse(body)!;
        root["generated"] = "2026-11-19T03:17:00Z";
        foreach (var card in root["p"]!.AsArray()) card!.AsArray()[8] = 0;
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static EditionGuard Guard(Db db, GuardLimits? limits = null) =>
        new(new HistoryReader(db, NullLogger<HistoryReader>.Instance), Options.Create(limits ?? new GuardLimits()), NullLogger<EditionGuard>.Instance);

    private static DigestFiles Digests(Db db, TimeProvider clock) =>
        new(new HistoryReader(db, NullLogger<HistoryReader>.Instance), clock, NullLogger<DigestFiles>.Instance);

    private static ImportService Service(Pages pages, EditionStore store, Db db, ExportFiles exports, GuardLimits? limits = null) => new(
        new EditionSource(new HttpClient(pages), new ConfigurationBuilder().AddInMemoryCollection([new("DATA_URL", "https://pages.example/data.json")]).Build()),
        store,
        Guard(db, limits),
        db,
        new DbKeyRepository(db, NullLogger<DbKeyRepository>.Instance),
        exports,
        Digests(db, TimeProvider.System),
        new NoOutputCache(),
        TimeProvider.System,
        NullLogger<ImportService>.Instance);

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
            Guard(TestDatabase.Open("")),
            TestDatabase.Open(""),
            new DbKeyRepository(TestDatabase.Open(""), NullLogger<DbKeyRepository>.Instance),
            exports,
            Digests(TestDatabase.Open(""), clock),
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
            Guard(database.Db),
            database.Db,
            new DbKeyRepository(database.Db, NullLogger<DbKeyRepository>.Instance),
            exports,
            Digests(database.Db, TimeProvider.System),
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
            Guard(database.Db),
            database.Db,
            keys,
            exports,
            Digests(database.Db, TimeProvider.System),
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

    [Fact]
    public async Task Edition_that_jumps_is_held_and_the_reason_stays_while_pages_answer_304()
    {
        byte[] good = await File.ReadAllBytesAsync(DigestEditionPath);
        var pages = new Pages { Body = good };
        var store = new EditionStore();
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        using var service = Service(pages, store, TestDatabase.Open(""), exports);
        Assert.True(await service.ImportOnceAsync(CancellationToken.None));
        var accepted = store.Current;

        pages.Body = Emptied(good);
        Assert.True(await service.ImportOnceAsync(CancellationToken.None));
        Assert.Same(accepted, store.Current);
        Assert.Equal("EditionGuard: changed-cards", store.Status.LastError);

        // той самий підмінений файл: 304, а причина не зникає
        Assert.True(await service.ImportOnceAsync(CancellationToken.None));
        Assert.Same(accepted, store.Current);
        Assert.Equal("EditionGuard: changed-cards", store.Status.LastError);

        // Pages перезібрали з доброго коду
        pages.Body = good;
        Assert.True(await service.ImportOnceAsync(CancellationToken.None));
        Assert.Null(store.Status.LastError);
    }

    [Fact]
    public async Task Held_edition_is_accepted_once_its_sha256_is_set_in_the_configuration()
    {
        byte[] good = await File.ReadAllBytesAsync(DigestEditionPath);
        byte[] emptied = Emptied(good);
        var pages = new Pages { Body = good };
        var store = new EditionStore();
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        var limits = new GuardLimits { AcceptSha256 = Convert.ToHexString(SHA256.HashData(emptied)) };
        using var service = Service(pages, store, TestDatabase.Open(""), exports, limits);
        await service.ImportOnceAsync(CancellationToken.None);

        pages.Body = emptied;
        await service.ImportOnceAsync(CancellationToken.None);

        Assert.Equal(DateTimeOffset.Parse("2026-11-19T03:17:00Z"), store.Current!.Edition.Generated);
        Assert.Null(store.Status.LastError);
    }

    [PostgresFact]
    public async Task After_a_restart_a_jump_is_measured_against_the_last_snapshot_and_nothing_is_served()
    {
        await using var database = await TestDatabase.CreateAsync();
        byte[] good = await File.ReadAllBytesAsync(DigestEditionPath);
        using var exports = new ExportFiles(NullLogger<ExportFiles>.Instance);
        using (var first = Service(new Pages { Body = good }, new EditionStore(), database.Db, exports))
            Assert.True(await first.ImportOnceAsync(CancellationToken.None));

        // новий екземпляр після сну: у пам'яті нічого, у базі знімок доброго випуску
        var store = new EditionStore();
        using var restarted = Service(new Pages { Body = Emptied(good) }, store, database.Db, exports);

        Assert.False(await restarted.ImportOnceAsync(CancellationToken.None));
        Assert.Null(store.Current);
        Assert.Equal("EditionGuard: changed-cards", store.Status.LastError);
        Assert.Equal(1L, await database.ScalarAsync<long>("select count(*) from snapshot"));
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
