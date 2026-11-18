using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

/// <summary>Причини /health/fresh на випуску з фікстур, зібраному 17.11.2026 о 22:00 UTC.</summary>
public class FreshnessTests
{
    private static readonly FreshnessLimits Limits = new();

    /// <summary>Через стільки годин після збірки випуску.</summary>
    private static DateTimeOffset At(double hours) => Fixtures.AsOf.AddHours(hours);

    private static async Task<EditionStore> PublishedAt(double hours)
    {
        var store = new EditionStore();
        store.Publish(await Fixtures.LoadedAsync(), At(hours));
        return store;
    }

    [Fact]
    public void Before_the_first_edition_the_reason_is_no_edition()
    {
        var store = new EditionStore();
        store.Failed(At(1), "HttpRequestException → IOException");

        var report = Freshness.Of(store, At(1), Limits);

        Assert.Equal(new FreshnessReport(false, "no-edition", null, null), report);
    }

    [Fact]
    public async Task Edition_built_two_hours_ago_is_fresh()
    {
        var store = await PublishedAt(1);

        var report = Freshness.Of(store, At(2), Limits);

        Assert.Equal(new FreshnessReport(true, null, At(1), 2.0), report);
    }

    [Fact]
    public async Task Held_edition_is_reported_at_once_and_stays_until_a_good_one_is_accepted()
    {
        var store = await PublishedAt(1);

        store.Held(At(2), "EditionGuard: changed-cards");
        Assert.Equal(new FreshnessReport(false, "guard-held", At(2), 2.0), Freshness.Of(store, At(2), Limits));

        // той самий файл далі віддається з 304
        store.Checked(At(3));
        Assert.Equal("guard-held", Freshness.Of(store, At(3), Limits).Reason);

        // збій мережі затирає причину в lastError, але запобіжник випуск і далі тримає
        store.Failed(At(4), "HttpRequestException → IOException");
        Assert.Equal("guard-held", Freshness.Of(store, At(4), Limits).Reason);
        Assert.Equal("guard-held", Freshness.Of(store, At(40), Limits).Reason);

        // Pages перезібрали з доброго коду
        store.Publish(await Fixtures.LoadedAsync(), At(5));
        Assert.True(Freshness.Of(store, At(5), Limits).Fresh);
    }

    [Fact]
    public void Edition_held_right_after_a_restart_is_guard_held_rather_than_no_edition()
    {
        var store = new EditionStore();
        store.Held(At(1), "EditionGuard: open-cards");

        var report = Freshness.Of(store, At(1), Limits);

        Assert.Equal(new FreshnessReport(false, "guard-held", At(1), null), report);
    }

    [Fact]
    public async Task Failures_within_the_limit_keep_it_fresh_and_longer_ones_are_import_failing()
    {
        var store = await PublishedAt(1);
        // наступна щогодинна спроба і повтор за нею не вдались
        store.Failed(At(2), "HttpRequestException → IOException");
        store.Failed(At(2.2), "TaskCanceledException");

        var within = Freshness.Of(store, At(4), Limits);
        var beyond = Freshness.Of(store, At(4.1), Limits);

        Assert.Equal(new FreshnessReport(true, null, At(1), 4.0), within);
        Assert.Equal(new FreshnessReport(false, "import-failing", At(1), 4.1), beyond);
    }

    [Fact]
    public async Task Answer_after_a_failure_resets_the_wait()
    {
        var store = await PublishedAt(1);
        store.Failed(At(2), "HttpRequestException: 503");
        store.Checked(At(3.5));

        var report = Freshness.Of(store, At(6), Limits);

        Assert.Equal(new FreshnessReport(true, null, At(3.5), 6.0), report);
    }

    [Fact]
    public async Task Edition_not_renewed_for_longer_than_the_limit_is_stale_although_pages_answer()
    {
        var store = await PublishedAt(1);
        store.Checked(At(36));
        Assert.True(Freshness.Of(store, At(36), Limits).Fresh);

        store.Checked(At(37));
        var report = Freshness.Of(store, At(37), Limits);

        Assert.Equal(new FreshnessReport(false, "stale", At(37), 37.0), report);
    }

    [Fact]
    public async Task Limits_are_the_ones_passed_in()
    {
        var store = await PublishedAt(1);
        store.Failed(At(4), "HttpRequestException: 404");

        var report = Freshness.Of(store, At(37), new FreshnessLimits { EditionAgeHours = 72, ImportFailingHours = 48 });

        Assert.True(report.Fresh);
    }

    [Fact]
    public async Task Edition_dated_a_little_ahead_of_the_service_clock_is_zero_hours_old()
    {
        var store = await PublishedAt(-0.01);

        var report = Freshness.Of(store, At(-0.01), Limits);

        Assert.Equal(0, report.EditionAgeHours);
        Assert.True(report.Fresh);
    }
}
