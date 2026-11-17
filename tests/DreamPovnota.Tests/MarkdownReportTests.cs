using DreamPovnota.Cards;
using DreamPovnota.Report;
using DreamPovnota.Stats;
using DreamPovnota.Store;
using static DreamPovnota.Tests.TestCards;

namespace DreamPovnota.Tests;

public class MarkdownReportTests
{
    private static string Report(params ProjectCard[] cards) =>
        MarkdownReport.Build(Summary.Build(cards, [], AsOf, new SyncInfo(AsOf, cards.Length + 1, 0, 0, ["x"], 0)));

    [Fact]
    public void Report_starts_with_the_snapshot_date_and_disclaimer()
    {
        string report = Report(Full(Stage.Idea));

        Assert.StartsWith("# Наповненість карток проєктів у DREAM", report);
        Assert.Contains("Дата зрізу: 17.11.2026.", report);
        Assert.Contains("не офіційна статистика", report);
    }

    [Fact]
    public void Check_table_shows_applicable_filled_and_missing()
    {
        string report = Report(Full(Stage.Appraised, "p1"), Full(Stage.Appraised, "p2") with { CostEstimate = 0 });

        Assert.Contains("| Оцінка | Кошторисна вартість | 2 | 1 | 50,0% | 1 |", report);
    }

    [Fact]
    public void Conditional_check_carries_its_condition()
    {
        Assert.Contains("Джерело фінансування (якщо є закупівля чи договір)", Report(Full(Stage.Implementation)));
    }

    [Fact]
    public void Feed_size_and_unparsed_come_from_sync_info()
    {
        string report = Report(Full(Stage.Idea));

        Assert.Contains("| У стрічці API | 2 |", report);
        Assert.Contains("| Не вдалося розібрати | 1 |", report);
    }

    [Fact]
    public void Large_numbers_use_ukrainian_digit_grouping()
    {
        var cards = Enumerable.Range(0, 1200).Select(i => Full(Stage.Idea, "p" + i)).ToArray();

        Assert.Contains("| Відкриті, беруть участь у перевірках | 1 200 |", Report(cards));
    }

    [Fact]
    public void Invalid_edrpou_is_listed()
    {
        string report = Report(Full(Stage.Idea, edrpou: "42750838"));

        Assert.Contains("не проходять перевірку контрольного розряду 1: 42750838", report);
    }

    [Fact]
    public void Report_has_no_long_dashes()
    {
        Assert.DoesNotContain("—", Report(Full(Stage.Implementation), Full(Stage.Idea, "p2")));
    }

    [Fact]
    public async Task Report_is_written_to_the_given_file()
    {
        using var dir = new TempDir();
        string path = dir.File("docs/report.md");

        await MarkdownReport.WriteAsync(Summary.Build([Full(Stage.Idea)], [], AsOf), path);

        Assert.Contains("## Стадії", await File.ReadAllTextAsync(path));
    }
}
