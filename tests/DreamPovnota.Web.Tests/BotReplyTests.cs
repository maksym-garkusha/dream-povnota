using DreamPovnota.Cards;
using DreamPovnota.Web.Live;
using DreamPovnota.Web.Telegram;
using DreamPovnota.Web.Web;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamPovnota.Web.Tests;

public class BotReplyTests
{
    private static readonly Uri Site = new("https://dream-povnota.example/");

    /// <summary>Справжня жива перевірка картки з фікстури через підставний DREAM.</summary>
    internal static async Task<(string Code, LiveOutcome Outcome)> CheckedAsync(string fixture)
    {
        string json = Fixtures.Text(fixture);
        string code = CardParser.ParseProject(json).Code["DREAM-UA-".Length..];
        var dream = new FakeDream { AnyObject = Fixtures.Text("object-active.json") }.Json("ideas/" + code, json);
        var http = new HttpClient(dream);
        LiveCheck.Configure(http);
        using var limits = new SharedLimits();
        var live = new LiveCheck(http, new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 }), limits, TimeProvider.System, NullLogger<LiveCheck>.Instance);
        return (code, await live.RunAsync(code, CancellationToken.None));
    }

    [Fact]
    public async Task Open_card_lists_filled_and_missing_fields_and_what_to_add_first()
    {
        var (code, outcome) = await CheckedAsync("project-active.json");
        var result = outcome.Result!;

        string text = BotReply.Check(code, outcome, Site, organisationOnSite: true);

        Assert.StartsWith(result.Title[..Math.Min(50, result.Title.Length)], text);
        Assert.Contains("Стадія: " + result.StageTitle, text);
        Assert.Contains($"Заповнено {result.ScoreFilled} з {result.ScoreApplicable} доречних полів.", text);
        foreach (var check in result.Filled.Where(c => c.CountsInScore)) Assert.Contains("• " + check.Title, text);
        foreach (var check in result.Missing.Where(c => c.CountsInScore)) Assert.Contains("• " + check.Title, text);
        var first = result.Missing.FirstOrDefault(c => c.CountsInScore);
        Assert.Contains(first is null ? "Усе, що доречне на цій стадії, заповнено." : "Дописати першим: " + first.Title, text);
        Assert.Contains("Картка в DREAM: https://dream.gov.ua/ua/project/DREAM-UA-" + code + "/profile", text);
        Assert.Contains("Усі картки організації: https://dream-povnota.example/org/" + result.Edrpou, text);
        Assert.EndsWith("\n" + BotReply.Disclaimer, text);
    }

    [Fact]
    public async Task Disclaimer_survives_when_a_long_reply_is_cut()
    {
        var (code, outcome) = await CheckedAsync("project-active.json");
        var result = outcome.Result!;
        // 400 рядків переліку роблять відповідь довшою за межу повідомлення Telegram
        var changed = outcome with { Result = result with { Missing = Enumerable.Repeat(result.Missing.Concat(result.Filled).First(), 400).ToList() } };

        string text = BotReply.Check(code, changed, Site, organisationOnSite: false);

        Assert.True(text.Length <= BotMessage.MaxLength);
        Assert.EndsWith("\n" + BotReply.Disclaimer, text);
    }

    [Fact]
    public async Task Only_fields_relevant_to_the_stage_are_listed()
    {
        var (code, outcome) = await CheckedAsync("project-idea.json");

        string text = BotReply.Check(code, outcome, Site, organisationOnSite: false);

        Assert.DoesNotContain("Кошторисна вартість", text);
        Assert.DoesNotContain("Закупівля або договір", text);
        Assert.DoesNotContain("Усі картки організації", text);
    }

    [Fact]
    public async Task English_fields_are_shown_apart_from_the_score()
    {
        var (code, outcome) = await CheckedAsync("project-english.json");

        string text = BotReply.Check(code, outcome, Site, organisationOnSite: false);

        Assert.Contains("Англійською, поза підрахунком:", text);
        Assert.DoesNotContain("Дописати першим: Назва англійською", text);
        Assert.DoesNotContain("Дописати першим: Опис англійською", text);
    }

    [Fact]
    public async Task Reply_has_no_name_or_tax_number_of_a_sole_proprietor()
    {
        var (code, outcome) = await CheckedAsync("project-individual.json");

        string text = BotReply.Check(code, outcome, Site, organisationOnSite: true);

        Assert.DoesNotContain("1234567899", text);
        Assert.DoesNotContain("Вигадан", text);
    }

    [Fact]
    public async Task Closed_card_is_not_scored()
    {
        var (code, outcome) = await CheckedAsync("project-cancelled.json");

        string text = BotReply.Check(code, outcome, Site, organisationOnSite: false);

        Assert.Contains("Картка закрита", text);
        Assert.DoesNotContain("Дописати першим", text);
    }

    [Fact]
    public async Task Markup_in_a_title_stays_plain_text_and_long_titles_are_cut()
    {
        var (code, outcome) = await CheckedAsync("project-active.json");
        string title = "<b>Школа</b> *№ 3* " + new string('я', 300);
        var changed = outcome with { Result = outcome.Result! with { Title = title } };

        string text = BotReply.Check(code, changed, Site, organisationOnSite: false);

        string firstLine = text[..text.IndexOf('\n')];
        Assert.StartsWith("<b>Школа</b> *№ 3* ", firstLine);
        Assert.Equal(BotReply.MaxTitle, firstLine.Length);
        Assert.EndsWith("…", firstLine);
    }

    [Fact]
    public async Task Title_is_not_cut_inside_an_emoji()
    {
        var (code, outcome) = await CheckedAsync("project-active.json");
        string title = new string('я', BotReply.MaxTitle - 2) + "🏫" + new string('я', 10);
        var changed = outcome with { Result = outcome.Result! with { Title = title } };

        string firstLine = BotReply.Check(code, changed, Site, organisationOnSite: false).Split('\n')[0];

        Assert.Equal(new string('я', BotReply.MaxTitle - 2) + "…", firstLine);
        Assert.DoesNotContain(firstLine, char.IsSurrogate);
    }

    [Theory]
    [InlineData(LiveStatus.NotFound, "у DREAM немає")]
    [InlineData(LiveStatus.TooManyObjects, "https://dream-povnota.example/check/010101-ABCDEF12")]
    [InlineData(LiveStatus.Busy, "Спробуйте за 5 хв")]
    [InlineData(LiveStatus.Unavailable, "DREAM зараз не відповідає")]
    public void Other_outcomes_are_explained(LiveStatus status, string expected)
    {
        var outcome = new LiveOutcome(status, RetryAfter: TimeSpan.FromSeconds(270));

        Assert.Contains(expected, BotReply.Check("010101-ABCDEF12", outcome, Site, organisationOnSite: false));
    }

    [Fact]
    public void Help_says_what_the_bot_does_and_that_it_is_not_official()
    {
        string text = BotReply.Help(Site);

        Assert.Contains(CodeInput.ExampleCode, text);
        Assert.Contains(BotReply.Disclaimer, text);
        Assert.Contains(Site.ToString(), text);
    }

    [Fact]
    public void Limit_reply_rounds_the_wait_up_to_minutes()
    {
        Assert.Contains("за 2 хв", BotReply.Limited(TimeSpan.FromSeconds(61)));
    }
}
