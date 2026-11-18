using System.Net;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
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
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Edition_loads_after_two_failed_attempts_within_seconds_not_an_hour()
    {
        var pages = new FlakyPages(failures: 2);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-18T01:17:00Z"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("DATA_URL", "https://pages.example/data.json")]).Build();
        var store = new EditionStore();
        using var service = new ImportService(
            new EditionSource(new HttpClient(pages), configuration),
            store,
            TestDatabase.Open(""),
            new NoOutputCache(),
            clock,
            NullLogger<ImportService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitFor(() => pages.Requests == 1);
        Assert.Null(store.Current);
        Assert.Equal("Error while copying content to a stream. → The response ended prematurely.", store.Status.LastError);

        clock.Advance(ImportService.RetryDelays[0]);
        await WaitFor(() => pages.Requests == 2);
        clock.Advance(ImportService.RetryDelays[1]);
        await WaitFor(() => store.Current is not null);

        Assert.Equal(3, pages.Requests);
        Assert.Null(store.Status.LastError);
        await service.StopAsync(CancellationToken.None);
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
