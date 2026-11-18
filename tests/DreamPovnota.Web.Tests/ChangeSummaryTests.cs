using System.Security.Cryptography;
using DreamPovnota.Export;
using DreamPovnota.Web.Changes;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

public class ChangeSummaryTests
{
    private static readonly RuleCheck[] Rules =
    [
        new("description", "Опис проєкту"),
        new("sector", "Сектор"),
        new("cost", "Кошторисна вартість"),
        new("fresh", "Оновлювалась за останній рік"),
        new("title-en", "Назва англійською"),
    ];

    private static readonly (string Key, string Title)[] Stages =
        [("idea", "Ідея"), ("defined", "Визначено"), ("appraised", "Оцінено"), ("impl", "Реалізація"), ("done", "Завершено")];

    private static readonly DateOnly Date = new(2026, 11, 18);

    // середа 18.11.2026, 03:00 за Києвом
    private static readonly DateTimeOffset First = new(2026, 11, 18, 1, 0, 0, TimeSpan.Zero);

    private static SnapshotMark Snapshot(int id, DateTimeOffset generated, IReadOnlyList<RuleCheck>? rules = null, bool rulesChanged = false) =>
        new(id, generated, rulesChanged, rules ?? Rules);

    private static int Bits(params int[] bits) => bits.Aggregate(0, (mask, bit) => mask | 1 << bit);

    private static CardTransition Changed(
        int period, int applicableFrom, int filledFrom, int filledTo, string? edrpou = "11111111",
        string stageFrom = "defined", string stageTo = "defined", int updatedDays = 0) =>
        new(period, false, edrpou, stageFrom, Date, applicableFrom, filledFrom, stageTo, Date.AddDays(updatedDays), filledTo);

    private static ChangeSummary Summarise(IReadOnlyList<SnapshotMark> snapshots, Func<IReadOnlyList<PlannedPeriod>, IEnumerable<CardTransition>> rows)
    {
        var plan = ChangeSummaryBuilder.Plan(snapshots);
        return ChangeSummaryBuilder.Build(snapshots, plan, rows(plan).ToList(), Stages, snapshots[^1].Generated.AddMinutes(1));
    }

    private static int Week(IReadOnlyList<PlannedPeriod> plan, int index = 0) => plan.Where(p => p.Kind == PeriodKind.Week).ElementAt(index).Number;

    [Fact]
    public void Empty_history_gives_no_periods()
    {
        var summary = ChangeSummaryBuilder.Build([], [], [], Stages, First);

        Assert.Equal(0, summary.Snapshots);
        Assert.Null(summary.HistoryFrom);
        Assert.Null(summary.Total);
        Assert.Empty(summary.Weeks);
        Assert.Empty(summary.FirstWeekDays);
    }

    [Fact]
    public void One_snapshot_gives_its_week_and_day_with_nothing_to_compare()
    {
        var summary = Summarise([Snapshot(1, First)], _ => []);

        var week = Assert.Single(summary.Weeks);
        Assert.Equal(new DateOnly(2026, 11, 16), week.From);
        Assert.Equal(new DateOnly(2026, 11, 22), week.To);
        Assert.Equal("2026-W47", week.IsoWeek);
        Assert.Equal(Date, week.ObservedFrom);
        Assert.Equal(1, week.Snapshots);
        Assert.Null(week.ComparedFrom);
        Assert.False(week.All.HasChanges);
        Assert.Equal(Date, Assert.Single(summary.FirstWeekDays).From);
        Assert.Null(summary.Total!.ComparedFrom);
    }

    [Fact]
    public void Kyiv_midnight_on_sunday_splits_the_weeks()
    {
        // 21:30 UTC у неділю це ще неділя за Києвом (UTC+2), 22:30 UTC уже понеділок
        var sundayLate = new DateTimeOffset(2026, 11, 22, 21, 30, 0, TimeSpan.Zero);
        var mondayEarly = new DateTimeOffset(2026, 11, 22, 22, 30, 0, TimeSpan.Zero);
        SnapshotMark[] snapshots = [Snapshot(1, First), Snapshot(2, sundayLate), Snapshot(3, mondayEarly)];

        var plan = ChangeSummaryBuilder.Plan(snapshots);
        var weeks = plan.Where(p => p.Kind == PeriodKind.Week).ToList();

        Assert.Equal(new DateOnly(2026, 11, 22), ChangeCalendar.KyivDate(sundayLate));
        Assert.Equal(new DateOnly(2026, 11, 23), ChangeCalendar.KyivDate(mondayEarly));
        Assert.Equal(2, weeks.Count);
        Assert.Equal((1, 2), (weeks[0].From!.Id, weeks[0].To!.Id));
        Assert.Equal((2, 3), (weeks[1].From!.Id, weeks[1].To!.Id));
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 22, 0, 0, TimeSpan.Zero), weeks[1].Span.Start);
    }

    [Fact]
    public void Week_boundary_follows_summer_time_in_kyiv()
    {
        // з 28.03.2027 Київ на UTC+3: північ понеділка це 21:00 UTC неділі
        Assert.Equal(new DateTimeOffset(2027, 3, 28, 21, 0, 0, TimeSpan.Zero), ChangeCalendar.Midnight(new DateOnly(2027, 3, 29)));
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 22, 0, 0, TimeSpan.Zero), ChangeCalendar.Midnight(new DateOnly(2026, 11, 23)));
        Assert.Equal("2027-W13", ChangeCalendar.IsoWeek(new DateOnly(2027, 3, 29)));
        Assert.Equal("2026-W53", ChangeCalendar.IsoWeek(new DateOnly(2026, 12, 28)));
    }

    [Fact]
    public void Days_cover_the_first_week_up_to_the_last_snapshot_and_keep_days_without_snapshots()
    {
        var friday = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var summary = Summarise([Snapshot(1, First), Snapshot(2, friday)], _ => []);

        Assert.Equal([18, 19, 20], summary.FirstWeekDays.Select(d => d.From.Day));
        Assert.Equal([1, 0, 1], summary.FirstWeekDays.Select(d => d.Snapshots));
        Assert.Null(summary.FirstWeekDays[1].ComparedFrom);
        Assert.Equal(First, summary.FirstWeekDays[2].ComparedFrom);
    }

    [Fact]
    public void Only_the_last_twelve_weeks_are_shown_and_days_go_with_the_first_week()
    {
        var late = First.AddDays(7 * 13);
        var summary = Summarise([Snapshot(1, First), Snapshot(2, late)], _ => []);

        Assert.Equal(ChangeCalendar.MaxWeeks, summary.Weeks.Count);
        Assert.Equal(ChangeCalendar.Monday(ChangeCalendar.KyivDate(late)), summary.Weeks[^1].From);
        Assert.Empty(summary.FirstWeekDays);
        Assert.Equal(summary.Weeks[0].From, summary.Total!.From);
    }

    [Fact]
    public void Field_counts_only_where_it_was_applicable_and_empty_and_freshness_is_not_a_field()
    {
        var summary = Summarise([Snapshot(1, First), Snapshot(2, First.AddHours(5))], plan =>
        [
            // сектор був доречний і порожній; кошторис на початок недоречний; «fresh» не поле
            Changed(Week(plan), applicableFrom: Bits(0, 1, 3), filledFrom: Bits(0), filledTo: Bits(0, 1, 2, 3)),
        ]);

        var counts = summary.Weeks[0].All;
        Assert.Equal(1, counts.FieldsFilled);
        Assert.Equal(1, counts.CardsWithFields);
        Assert.Equal([new FieldCount("sector", "Сектор", 1)], counts.Fields);
        Assert.Equal(0, counts.UpdatedCards);
    }

    [Fact]
    public void Updated_new_and_stage_moves_are_counted_per_card()
    {
        var summary = Summarise([Snapshot(1, First), Snapshot(2, First.AddHours(5))], plan =>
        [
            Changed(Week(plan), Bits(0), Bits(0), Bits(0), updatedDays: 2),
            Changed(Week(plan), Bits(0), Bits(0), Bits(0), stageFrom: "idea", stageTo: "defined", updatedDays: 1),
            Changed(Week(plan), Bits(0), Bits(0), Bits(0), stageFrom: "defined", stageTo: "appraised"),
            Changed(Week(plan), Bits(0), Bits(0), Bits(0), stageFrom: "idea", stageTo: "defined"),
            new(Week(plan), true, "11111111", null, null, null, null, "idea", Date, Bits(0, 1)),
            // повернулась до відкритих: ні нова, ні оновлена
            new(Week(plan), false, "11111111", null, null, null, null, "idea", Date, Bits(0)),
        ]);

        var counts = summary.Weeks[0].All;
        Assert.Equal(2, counts.UpdatedCards);
        Assert.Equal(1, counts.NewCards);
        Assert.Equal(3, counts.StageMoves);
        Assert.Equal(0, counts.FieldsFilled);
        Assert.Equal(
            [new StageMove("idea", "Ідея", "defined", "Визначено", 2), new StageMove("defined", "Визначено", "appraised", "Оцінено", 1)],
            counts.Stages);
    }

    [Fact]
    public void Fields_are_matched_by_code_when_the_list_of_checks_changes()
    {
        RuleCheck[] reordered = [Rules[1], new("purpose", "Мета"), Rules[0], Rules[2], Rules[3], Rules[4]];
        SnapshotMark[] snapshots = [Snapshot(1, First), Snapshot(2, First.AddHours(5), reordered, rulesChanged: true)];

        var summary = Summarise(snapshots, plan =>
        [
            // на початок: опис (біт 0) заповнено, сектор (біт 1) порожній; на кінець сектор біт 0, мета біт 1, опис біт 2
            Changed(Week(plan), applicableFrom: Bits(0, 1), filledFrom: Bits(0), filledTo: Bits(0, 1, 2)),
        ]);

        var week = summary.Weeks[0];
        Assert.True(week.RulesChanged);
        Assert.Equal(["sector"], week.All.Fields.Select(f => f.Check));
    }

    [Fact]
    public void Services_are_summed_and_the_fastest_three_are_named_in_list_order_on_ties()
    {
        const string agency = "37641918", vinnytsia = "25845655", kyiv = "26345736", lviv = "25253009";
        var summary = Summarise([Snapshot(1, First), Snapshot(2, First.AddHours(5))], plan =>
        [
            Changed(Week(plan), Bits(0, 1, 2), 0, Bits(0, 1, 2), edrpou: lviv),
            Changed(Week(plan), Bits(0, 1), 0, Bits(0, 1), edrpou: kyiv),
            Changed(Week(plan), Bits(0, 1), 0, Bits(0, 1), edrpou: vinnytsia),
            Changed(Week(plan), Bits(0), 0, Bits(0), edrpou: agency),
            Changed(Week(plan), Bits(0, 1, 2), 0, Bits(0, 1, 2), edrpou: "11111111"),
            Changed(Week(plan), Bits(0), Bits(0), Bits(0), edrpou: kyiv, updatedDays: 1),
        ]);

        var week = summary.Weeks[0];
        Assert.Equal(11, week.All.FieldsFilled);
        Assert.Equal(8, week.Services.FieldsFilled);
        Assert.Equal(4, week.Services.CardsWithFields);
        Assert.Equal(1, week.Services.UpdatedCards);
        // у Вінницькій і Київській по два поля, а Вінницька в переліку раніше
        Assert.Equal([lviv, vinnytsia, kyiv], week.Fastest.Select(s => s.Edrpou));
        Assert.Equal(new ServiceExample(lviv, "Служба відновлення та розвитку інфраструктури у Львівській області", 3, 1), week.Fastest[0]);
    }

    [Fact]
    public void A_week_without_new_fields_names_no_service()
    {
        var summary = Summarise([Snapshot(1, First), Snapshot(2, First.AddHours(5))], plan =>
            [Changed(Week(plan), Bits(0), Bits(0), Bits(0), edrpou: "26345736", updatedDays: 1)]);

        Assert.Equal(1, summary.Weeks[0].Services.UpdatedCards);
        Assert.Empty(summary.Weeks[0].Fastest);
    }

    [Fact]
    public void Week_in_progress_is_flagged_by_the_clock()
    {
        SnapshotMark[] snapshots = [Snapshot(1, First), Snapshot(2, First.AddHours(5))];
        var plan = ChangeSummaryBuilder.Plan(snapshots);

        var during = ChangeSummaryBuilder.Build(snapshots, plan, [], Stages, First.AddDays(1));
        var after = ChangeSummaryBuilder.Build(snapshots, plan, [], Stages, new DateTimeOffset(2026, 11, 22, 22, 0, 0, TimeSpan.Zero));

        Assert.True(during.Weeks[0].InProgress);
        Assert.False(after.Weeks[0].InProgress);
    }

    // З базою: знімки пише той самий SnapshotWriter, що й імпорт, а зміни між ними відомі наперед.
    // 1: середа 18.11, 03:00 за Києвом; 2: того ж дня 12:00; 3: неділя 22.11, 23:30; 4: понеділок 23.11, 00:30.
    [PostgresFact]
    public async Task Weeks_and_days_from_a_written_history_count_each_card_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);
        var start = await Fixtures.LoadedAsync();
        var edition = WithService(start.Edition, out int a);
        var cards = edition.Cards.ToList();
        int bit = EmptyField(edition, cards[a]);
        // b і c ведуть інші організації, щоб у зріз служб потрапила тільки картка a
        bool Other(int i) => cards[i].Organisation != cards[a].Organisation;
        int b = Enumerable.Range(0, cards.Count).First(Other);
        int c = Enumerable.Range(0, cards.Count).First(i => Other(i) && i != b && cards[i].Stage != cards[a].Stage);

        async Task Write(DateTimeOffset generated, List<EditionCard> list) =>
            Assert.NotNull(await SnapshotWriter.WriteAsync(
                database.Db, new LoadedEdition(edition with { Generated = generated, Cards = list.ToList() }, SHA256.HashData(Guid.NewGuid().ToByteArray())), CancellationToken.None));

        await Write(First, cards);
        cards[a] = cards[a] with { Filled = cards[a].Filled | 1 << bit };
        cards[b] = cards[b] with { Updated = cards[b].Updated.AddDays(1) };
        await Write(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero), cards);
        cards[a] = cards[a] with { Updated = cards[a].Updated.AddDays(1) };
        cards[b] = cards[b] with { Updated = cards[b].Updated.AddDays(1) };
        cards.Add(cards[c] with { Code = "010101-ABCDEF12", Title = "Нова картка" });
        await Write(new DateTimeOffset(2026, 11, 22, 21, 30, 0, TimeSpan.Zero), cards);
        cards[c] = cards[c] with { Stage = cards[a].Stage };
        cards[b] = cards[b] with { Updated = cards[b].Updated.AddDays(1) };
        await Write(new DateTimeOffset(2026, 11, 22, 22, 30, 0, TimeSpan.Zero), cards);

        var stages = edition.Stages.Select(s => (s.Key, s.Title)).ToList();
        var summary = await ChangeSummaryBuilder.LoadAsync(database.Reader, stages, new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(4, summary.Snapshots);
        Assert.Equal(["2026-W47", "2026-W48"], summary.Weeks.Select(w => w.IsoWeek));
        var (w47, w48) = (summary.Weeks[0], summary.Weeks[1]);
        // картку b оновлювали двічі за тиждень, а рахується вона один раз
        Assert.Equal((2, 1, 1, 0), (w47.All.UpdatedCards, w47.All.FieldsFilled, w47.All.NewCards, w47.All.StageMoves));
        Assert.Equal((1, 0, 0, 1), (w48.All.UpdatedCards, w48.All.FieldsFilled, w48.All.NewCards, w48.All.StageMoves));
        Assert.True(w48.InProgress);
        Assert.Equal(edition.Checks[bit].Code, Assert.Single(w47.All.Fields).Check);
        Assert.Equal((1, 1), (w47.Services.UpdatedCards, w47.Services.FieldsFilled));
        Assert.Equal("26345736", Assert.Single(w47.Fastest).Edrpou);
        Assert.False(w48.Services.HasChanges);
        Assert.Empty(w48.Fastest);

        var total = summary.Total!;
        Assert.Equal((2, 1, 1, 1), (total.All.UpdatedCards, total.All.FieldsFilled, total.All.NewCards, total.All.StageMoves));
        Assert.Equal([18, 19, 20, 21, 22], summary.FirstWeekDays.Select(d => d.From.Day));
        var (wednesday, sunday) = (summary.FirstWeekDays[0], summary.FirstWeekDays[4]);
        Assert.Equal((1, 1, 0), (wednesday.All.UpdatedCards, wednesday.All.FieldsFilled, wednesday.All.NewCards));
        Assert.Equal((2, 0, 1), (sunday.All.UpdatedCards, sunday.All.FieldsFilled, sunday.All.NewCards));
        Assert.All(summary.FirstWeekDays.Skip(1).Take(3), d => Assert.False(d.All.HasChanges));
    }

    [PostgresFact]
    public async Task Empty_history_in_the_database_gives_an_empty_summary()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);

        var summary = await ChangeSummaryBuilder.LoadAsync(database.Reader, [], First, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.Snapshots);
        Assert.Empty(summary.Weeks);
    }

    /// <summary>Організацію першої картки з порожнім доречним полем робимо Київською службою відновлення.</summary>
    private static Edition WithService(Edition edition, out int card)
    {
        card = Enumerable.Range(0, edition.Cards.Count).First(i => EmptyField(edition, edition.Cards[i]) >= 0);
        var organisations = edition.Organisations.ToList();
        int index = edition.Cards[card].Organisation;
        organisations[index] = organisations[index] with { Key = "26345736", Edrpou = "26345736" };
        return edition with { Organisations = organisations };
    }

    private static int EmptyField(Edition edition, EditionCard card)
    {
        int empty = card.Applicable & ~card.Filled;
        for (int bit = 0; bit < edition.Checks.Count; bit++)
            if ((empty & 1 << bit) != 0 && edition.Checks[bit].Code != "fresh") return bit;
        return -1;
    }
}
