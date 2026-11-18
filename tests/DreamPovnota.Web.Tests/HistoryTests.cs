using DreamPovnota.Export;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

public class HistoryTests
{
    private static async Task<LoadedEdition> Edition(Func<Edition, Edition>? change = null)
    {
        var loaded = await Fixtures.LoadedAsync();
        var edition = change is null ? loaded.Edition : change(loaded.Edition);
        return new LoadedEdition(edition, System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
    }

    private static async Task<int?> Import(TestDatabase database, LoadedEdition edition)
    {
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        return await SnapshotWriter.WriteAsync(database.Db, edition, CancellationToken.None);
    }

    [PostgresFact]
    public async Task Reading_history_before_the_first_migration_does_not_mark_the_database_down()
    {
        await using var database = await TestDatabase.CreateAsync();

        var status = await database.Reader.StatusAsync(CancellationToken.None);

        Assert.Null(status);
        Assert.True(database.Db.IsAvailable);
    }

    [PostgresFact]
    public async Task Migrations_apply_twice_without_error()
    {
        await using var database = await TestDatabase.CreateAsync();

        int first = await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        int second = await Migrator.MigrateAsync(database.Db, CancellationToken.None);

        Assert.Equal(Migrator.Migrations.Count, first);
        Assert.Equal(0, second);
    }

    [PostgresFact]
    public async Task Same_edition_imported_twice_gives_one_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var edition = await Edition();

        Assert.Equal(1, await Import(database, edition));
        Assert.Null(await Import(database, edition));
        Assert.Equal(1L, await database.ScalarAsync<long>("select count(*) from snapshot"));
    }

    [PostgresFact]
    public async Task Concurrent_imports_of_one_edition_give_one_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var edition = await Edition();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);

        var results = await Task.WhenAll(
            SnapshotWriter.WriteAsync(database.Db, edition, CancellationToken.None),
            SnapshotWriter.WriteAsync(database.Db, edition, CancellationToken.None));

        Assert.Single(results, r => r is not null);
        Assert.Equal(1L, await database.ScalarAsync<long>("select count(*) from snapshot"));
    }

    [PostgresFact]
    public async Task Older_edition_after_a_newer_one_is_ignored()
    {
        await using var database = await TestDatabase.CreateAsync();
        var newer = await Edition(e => e with { Generated = e.Generated.AddDays(1) });

        await Import(database, newer);

        Assert.Null(await Import(database, await Edition()));
    }

    [PostgresFact]
    public async Task Changed_added_and_removed_cards_are_stored_as_versions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await Edition();
        var cards = first.Edition.Cards;
        Assert.True(cards.Count >= 5);

        var second = await Edition(e =>
        {
            var next = cards.Skip(1).ToList();                                   // перша картка зникла
            next[0] = next[0] with { Filled = next[0].Filled & (next[0].Filled - 1) };   // найнижче заповнене поле спорожніло
            next[1] = next[1] with { Updated = next[1].Updated.AddDays(3) };
            next[2] = next[2] with { SeeksFunding = !next[2].SeeksFunding };
            next.Add(next[3] with { Code = "010101-ABCDEF12", Title = "Нова картка" });
            return e with { Generated = e.Generated.AddDays(1), Cards = next };
        });

        await Import(database, first);
        int? snapshot = await Import(database, second);

        var counts = await database.ScalarAsync<string>("select concat_ws(',', added, removed, changed) from snapshot where id = @snapshot", new { snapshot });
        Assert.Equal("1,1,3", counts);
        Assert.Equal((long)cards.Count + 1 + 3, await database.ScalarAsync<long>("select count(*) from card_version"));

        var changes = await database.Reader.ChangesAsync(new HistoryScope(), 1, CancellationToken.None);
        Assert.NotNull(changes);
        Assert.Equal(5, changes.Count);
        Assert.Contains(changes, c => c.Kind == ChangeKind.Added && c.Code == "010101-ABCDEF12");
        Assert.Contains(changes, c => c.Kind == ChangeKind.Removed && c.Code == cards[0].Code);
        int emptiedBit = System.Numerics.BitOperations.TrailingZeroCount(cards[1].Filled);
        Assert.Equal([first.Edition.Checks[emptiedBit].Code], changes.Single(c => c.Code == cards[1].Code).NoLongerFilled);
    }

    [PostgresFact]
    public async Task Rules_change_re_versions_every_card_and_is_flagged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await Edition();
        var second = await Edition(e => e with
        {
            Generated = e.Generated.AddDays(1),
            Checks = [.. e.Checks, e.Checks[0] with { Code = "new-check", Title = "Нова перевірка" }],
        });

        await Import(database, first);
        int? snapshot = await Import(database, second);

        Assert.True(await database.ScalarAsync<bool>("select rules_changed from snapshot where id = @snapshot", new { snapshot }));
        Assert.Equal((long)first.Edition.Cards.Count, await database.ScalarAsync<long>("select changed from snapshot where id = @snapshot", new { snapshot }));
        var changes = await database.Reader.ChangesAsync(new HistoryScope(), 5, CancellationToken.None);
        Assert.Empty(changes!);
    }

    [PostgresFact]
    public async Task Region_average_from_card_versions_matches_the_file()
    {
        await using var database = await TestDatabase.CreateAsync();
        var edition = await Edition();
        int? snapshot = await Import(database, edition);

        foreach (var region in edition.Edition.Regions)
        {
            double average = await database.ScalarAsync<double>(
                """
                select avg(case when score_applicable = 0 then 0 else score_filled::float8 / score_applicable end)
                from card_version where valid_during @> @snapshot and region = @key
                """, new { snapshot, key = region.Key });
            Assert.Equal(region.AverageScore, average, 1e-6);
        }
    }

    [PostgresFact]
    public async Task Organisation_trend_has_a_point_per_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await Edition();
        await Import(database, first);
        await Import(database, await Edition(e => e with { Generated = e.Generated.AddDays(1) }));
        var organisation = first.OrganisationList[0];

        var trend = await database.Reader.OrganisationTrendAsync(organisation.Key, CancellationToken.None);

        Assert.Equal(2, trend!.Count);
        Assert.All(trend, p => Assert.Equal(organisation.Open, p.Open));
        Assert.All(trend, p => Assert.Equal(organisation.AverageScore, p.AvgScore, 1e-6));
    }

    [PostgresFact]
    public async Task Pages_show_history_when_the_database_is_there()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = new WebFactory { DatabaseUrl = database.Url };
        var client = await factory.ReadyClientAsync();
        var organisation = (await Fixtures.LoadedAsync()).OrganisationList[0];

        string html = "";
        for (int i = 0; i < 50 && !html.Contains("Історію змін збираємо з"); i++)
        {
            await Task.Delay(100);
            html = await client.GetStringAsync("/org/" + Uri.EscapeDataString(organisation.Key));
        }

        Assert.Contains("Історію змін збираємо з", html);
        var feed = await client.GetAsync("/feeds/org/" + Uri.EscapeDataString(organisation.Key) + ".atom");
        Assert.Equal("application/atom+xml", feed.Content.Headers.ContentType!.MediaType);
        System.Xml.Linq.XDocument.Parse(await feed.Content.ReadAsStringAsync());
    }
}
