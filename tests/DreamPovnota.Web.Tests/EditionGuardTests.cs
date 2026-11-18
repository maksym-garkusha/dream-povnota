using DreamPovnota.Export;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

/// <summary>Правила запобіжника на випусках зі 100 карток, щоб пороги в 5 і 20 % були цілими картками.</summary>
public class EditionGuardTests
{
    private static readonly GuardLimits Limits = new();

    private static readonly EditionCheck[] TwoChecks = [Check("description"), Check("cost")];

    private static EditionCheck Check(string code) => new(code, code, "Основне", true, "idea", "done", null, "");

    /// <summary>100 карток, по 5 у кожної з 20 організацій; в усіх обидві перевірки доречні, заповнена перша.</summary>
    private static Edition Edition() => new(
        DateTimeOffset.Parse("2026-11-18T03:17:00Z"),
        null,
        TwoChecks,
        new EditionSlice("all", "Усі", 100, 0, 0.5, [new(100, 100), new(100, 0)]),
        [],
        [],
        [],
        Enumerable.Range(0, 20).Select(i => new EditionOrganisation($"{10000000 + i}", $"{10000000 + i}", $"Рада {i}", 5, 0.5)).ToList(),
        Enumerable.Range(0, 100).Select(i => Card(i, i / 5)).ToList());

    private static EditionCard Card(int number, int organisation) =>
        new($"{number + 1:000000}-AAAAAAAA", $"Картка {number}", organisation, 0, 0, 0, new DateOnly(2026, 11, 1), 0b11, 0b01, false);

    private static Edition Change(Edition edition, int count, Func<EditionCard, EditionCard> change) =>
        edition with { Cards = edition.Cards.Select((c, i) => i < count ? change(c) : c).ToList() };

    private static Jump? Compare(Edition before, Edition after) => EditionGuard.Compare(GuardBaseline.Of(before), after, Limits);

    [Fact]
    public void Same_edition_passes()
    {
        var edition = Edition();

        Assert.Null(Compare(edition, edition));
    }

    [Fact]
    public void A_day_of_ordinary_changes_passes()
    {
        var before = Edition();
        // 4 картки дозаповнили, 2 закрили, 3 нові: 9 змінених зі 100
        var after = Change(before, 4, c => c with { Filled = 0b11 });
        after = after with { Cards = after.Cards.Skip(2).Concat(Enumerable.Range(100, 3).Select(i => Card(i, 0))).ToList() };

        Assert.Null(Compare(before, after));
    }

    [Theory]
    [InlineData(105, null)]
    [InlineData(106, JumpKind.OpenCards)]
    [InlineData(95, null)]
    [InlineData(94, JumpKind.OpenCards)]
    public void Open_cards_may_change_by_five_percent_at_most(int open, JumpKind? expected)
    {
        var before = Edition();
        // нові картки в останньої організації, тож кількість організацій та сама
        var after = before with
        {
            Cards = open > 100
                ? before.Cards.Concat(Enumerable.Range(100, open - 100).Select(i => Card(i, 19))).ToList()
                : before.Cards.Take(open).ToList(),
        };

        Assert.Equal(expected, Compare(before, after)?.Kind);
    }

    [Fact]
    public void Cards_moved_to_new_organisations_trip_the_organisation_count()
    {
        var before = Edition();
        var organisations = before.Organisations.Concat([new EditionOrganisation("n:Нова", null, "Нова", 1, 0.5), new EditionOrganisation("n:Ще одна", null, "Ще одна", 1, 0.5)]).ToList();
        // 20 організацій стало 22, а маски карток ті самі
        var after = before with
        {
            Organisations = organisations,
            Cards = before.Cards.Select((c, i) => i switch { 0 => c with { Organisation = 20 }, 1 => c with { Organisation = 21 }, _ => c }).ToList(),
        };

        var jump = Compare(before, after);

        Assert.Equal(JumpKind.Organisations, jump?.Kind);
        Assert.Equal((20, 22), (jump!.Before, jump.After));
        Assert.Equal("EditionGuard: organisations", jump.Reason);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void More_than_a_fifth_of_cards_with_flipped_fields_is_a_jump(int flipped, bool jumps)
    {
        var before = Edition();
        var after = Change(before, flipped, c => c with { Filled = 0 });

        var jump = Compare(before, after);

        Assert.Equal(jumps, jump is not null);
        if (jumps) Assert.Equal(("EditionGuard: changed-cards", 21), (jump!.Reason, jump.After));
    }

    [Fact]
    public void Same_counts_with_every_code_replaced_is_a_jump()
    {
        var before = Edition();
        var after = before with { Cards = before.Cards.Select(c => c with { Code = c.Code.Replace("AAAAAAAA", "BBBBBBBB", StringComparison.Ordinal) }).ToList() };

        var jump = Compare(before, after);

        // 100 нових і 100 зниклих
        Assert.Equal((JumpKind.ChangedCards, 200), (jump?.Kind, jump?.After));
    }

    [Fact]
    public void A_new_check_and_reordered_bits_do_not_count_as_changes()
    {
        var before = Edition();
        // нова перевірка першою, решта зсунулась на біт: description тепер біт 1, cost біт 2
        var after = before with
        {
            Checks = [Check("english"), .. TwoChecks],
            Cards = before.Cards.Select(c => c with { Applicable = (c.Applicable << 1) | 1, Filled = c.Filled << 1 }).ToList(),
        };

        Assert.Null(Compare(before, after));
    }

    [Fact]
    public void Stage_change_that_makes_a_field_applicable_counts_as_a_change()
    {
        var before = Edition() with { Cards = Edition().Cards.Select(c => c with { Applicable = 0b01 }).ToList() };
        var after = Change(before, 21, c => c with { Applicable = 0b11 });

        Assert.Equal(JumpKind.ChangedCards, Compare(before, after)?.Kind);
    }

    [PostgresFact]
    public async Task Baseline_from_the_database_is_the_last_snapshot_as_it_was_in_memory()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        Assert.Null(await database.Reader.GuardBaselineAsync(CancellationToken.None));

        var current = await Fixtures.LoadedAsync();
        // днем раніше була ще одна картка: її закрита версія в порівняння не входить
        var extra = current.Edition.Cards[0] with { Code = "010125-BBBBBBBB" };
        var older = new LoadedEdition(
            current.Edition with { Generated = current.Edition.Generated.AddDays(-1), Cards = [.. current.Edition.Cards, extra] },
            new byte[32]);
        await SnapshotWriter.WriteAsync(database.Db, older, CancellationToken.None);
        await SnapshotWriter.WriteAsync(database.Db, current, CancellationToken.None);

        var stored = await database.Reader.GuardBaselineAsync(CancellationToken.None);
        var inMemory = GuardBaseline.Of(current.Edition);

        Assert.NotNull(stored);
        Assert.Equal((inMemory.Open, inMemory.Organisations), (stored.Open, stored.Organisations));
        Assert.Equal(inMemory.Checks, stored.Checks);
        Assert.Equal(inMemory.Cards.OrderBy(c => c.Key, StringComparer.Ordinal), stored.Cards.OrderBy(c => c.Key, StringComparer.Ordinal));
        Assert.Null(EditionGuard.Compare(stored, current.Edition, Limits));
    }
}
