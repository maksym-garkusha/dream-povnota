using DreamPovnota.Web.Live;

namespace DreamPovnota.Web.Tests;

public class CodeInputTests
{
    [Theory]
    [InlineData("DREAM-UA-050923-B400F7B2")]
    [InlineData("050923-B400F7B2")]
    [InlineData("  dream-ua-050923-b400f7b2 ")]
    [InlineData("https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/profile")]
    [InlineData("https://dream.gov.ua/en/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://www.dream.gov.ua/ua/project/dream-ua-050923-b400f7b2/profile?tab=docs#top")]
    [InlineData("https://DREAM.GOV.UA/ua/project/050923-B400F7B2/")]
    public void Code_or_portal_address_gives_the_upper_case_code(string input)
    {
        Assert.True(CodeInput.TryParse(input, out string? code, out _));
        Assert.Equal("050923-B400F7B2", code);
    }

    [Theory]
    [InlineData("https://dream.gov.ua.evil.com/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua@evil.com/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://user:pass@dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua:8443/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://evil.com/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua/ua%2Fproject/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua/ua/project%2FDREAM-UA-050923-B400F7B2")]
    [InlineData("https://dr\u0435am.gov.ua/ua/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua/de/project/DREAM-UA-050923-B400F7B2")]
    [InlineData("https://dream.gov.ua/ua/projects/DREAM-UA-050923-B400F7B2")]
    [InlineData("file:///etc/passwd")]
    [InlineData("DREAM-UA-050923-B400F7B")]
    [InlineData("DREAM-UA-050923-B400F7B2X")]
    [InlineData("050923-B400F7G2")]
    [InlineData("../ideas/050923-B400F7B2")]
    public void Anything_else_is_rejected_with_an_example(string input)
    {
        Assert.False(CodeInput.TryParse(input, out string? code, out string? error));
        Assert.Null(code);
        Assert.Equal(CodeInput.Example, error);
    }

    [Fact]
    public void Plain_http_address_is_rejected_with_a_hint_about_https()
    {
        Assert.False(CodeInput.TryParse("http://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2", out _, out string? error));
        Assert.Contains("https://", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_asks_for_a_code(string? input)
    {
        Assert.False(CodeInput.TryParse(input, out _, out string? error));
        Assert.StartsWith("Вкажіть", error);
    }

    [Fact]
    public void Very_long_input_is_rejected_before_parsing()
    {
        string input = "https://dream.gov.ua/ua/project/DREAM-UA-050923-B400F7B2/" + new string('a', 10_000);

        Assert.False(CodeInput.TryParse(input, out _, out string? error));
        Assert.Contains("300", error);
    }
}
