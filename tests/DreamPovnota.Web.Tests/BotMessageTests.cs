using DreamPovnota.Web.Live;
using DreamPovnota.Web.Telegram;

namespace DreamPovnota.Web.Tests;

public class BotMessageTests
{
    [Theory]
    [InlineData("DREAM-UA-050923-B400F7B2")]
    [InlineData("  dream-ua-050923-b400f7b2 \n")]
    [InlineData("050923-B400F7B2")]
    [InlineData("https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile")]
    [InlineData("https://dream.gov.ua/en/project/DREAM-UA-050923-B400F7B2/summary")]
    public void Code_or_link_is_a_check(string text)
    {
        var request = BotMessage.Parse(text);

        Assert.Equal(BotIntent.Check, request.Intent);
        Assert.Equal("050923-B400F7B2", request.Code);
    }

    [Theory]
    [InlineData("Перевірте, будь ласка: https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile. Дякую!")]
    [InlineData("Картка DREAM-UA-050923-B400F7B2, яку обговорювали")]
    [InlineData("(DREAM-UA-050923-B400F7B2)")]
    public void Code_inside_a_sentence_is_found(string text)
    {
        Assert.Equal("050923-B400F7B2", BotMessage.Parse(text).Code);
    }

    [Theory]
    [InlineData("/check DREAM-UA-050923-B400F7B2")]
    [InlineData("/check@dream_povnota_bot 050923-B400F7B2")]
    [InlineData("/CHECK https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile")]
    [InlineData("/start 050923-B400F7B2")]
    public void Check_command_and_start_link_carry_the_code(string text)
    {
        var request = BotMessage.Parse(text);

        Assert.Equal(BotIntent.Check, request.Intent);
        Assert.Equal("050923-B400F7B2", request.Code);
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/help")]
    [InlineData("/help@dream_povnota_bot")]
    [InlineData("/start something-else")]
    public void Start_and_help_ask_for_help(string text)
    {
        Assert.Equal(BotIntent.Help, BotMessage.Parse(text).Intent);
    }

    [Fact]
    public void Check_without_a_code_explains_the_command()
    {
        var request = BotMessage.Parse("/check");

        Assert.Equal(BotIntent.Invalid, request.Intent);
        Assert.Equal(BotMessage.CheckWithoutCode, request.Error);
    }

    [Fact]
    public void Unknown_command_is_invalid()
    {
        Assert.Equal(BotMessage.UnknownCommand, BotMessage.Parse("/settings").Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("привіт")]
    [InlineData("DREAM-UA-050923-B400F7")]
    [InlineData("https://dream.gov.ua.evil.com/ua/project/DREAM-UA-050923-B400F7B2/profile")]
    [InlineData("https://evil.com/?u=https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2")]
    public void Junk_gets_the_format_hint(string? text)
    {
        var request = BotMessage.Parse(text);

        Assert.Equal(BotIntent.Invalid, request.Intent);
        Assert.Null(request.Code);
        Assert.Equal(CodeInput.Example, request.Error);
    }

    [Fact]
    public void Plain_http_link_is_told_to_use_https()
    {
        var request = BotMessage.Parse("http://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile");

        Assert.Equal(BotIntent.Invalid, request.Intent);
        Assert.Contains("https://dream.gov.ua", request.Error);
    }

    [Fact]
    public void Long_text_without_a_code_gets_the_format_hint()
    {
        Assert.Equal(CodeInput.Example, BotMessage.Parse(new string('а', 1000)).Error);
    }

    [Fact]
    public void Text_longer_than_telegram_allows_is_not_searched()
    {
        string text = "DREAM-UA-050923-B400F7B2 " + new string('а', BotMessage.MaxLength);

        Assert.Equal(BotIntent.Invalid, BotMessage.Parse(text).Intent);
    }

    [Fact]
    public void Code_far_down_a_long_message_is_not_searched_for()
    {
        string text = string.Concat(Enumerable.Repeat("слово ", 100)) + "DREAM-UA-050923-B400F7B2";

        Assert.Equal(BotIntent.Invalid, BotMessage.Parse(text).Intent);
    }
}
