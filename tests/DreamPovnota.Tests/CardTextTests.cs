using DreamPovnota.Cards;

namespace DreamPovnota.Tests;

public class CardTextTests
{
    [Fact]
    public void Clean_strips_tags_entities_and_extra_spaces()
    {
        string html = "<p style=\"text-align: justify;\">Капітальний&nbsp;ремонт</p>\r\n<p>школи  №&nbsp;3</p>";

        Assert.Equal("Капітальний ремонт школи № 3", CardText.Clean(html));
    }

    [Fact]
    public void Clean_keeps_words_from_neighbouring_paragraphs_apart()
    {
        Assert.Equal("один два", CardText.Clean("<p>один</p><p>два</p>"));
    }

    [Theory]
    [InlineData("<p>NULL</p>")]
    [InlineData("null")]
    [InlineData(" - ")]
    [InlineData("<p>.</p>")]
    [InlineData("n/a")]
    [InlineData("Немає")]
    [InlineData("<b>НЕМА</b>")]
    public void Placeholder_is_a_stub(string html)
    {
        Assert.Equal(TextState.Stub, CardText.Classify(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p><br></p>")]
    public void Nothing_but_markup_is_empty(string? html)
    {
        Assert.Equal(TextState.Empty, CardText.Classify(html));
    }

    [Fact]
    public void Dashes_longer_than_one_are_not_in_the_stub_list()
    {
        // так рахує еталон: заглушка це точний збіг зі списком
        Assert.Equal(TextState.Filled, CardText.Classify("---"));
    }

    [Fact]
    public void Description_shorter_than_threshold_is_too_short()
    {
        // 29 знаків після чищення
        Assert.Equal(TextState.TooShort, CardText.Classify("<p>Капітальний ремонт даху школи</p>", 30));
        Assert.Equal(TextState.Filled, CardText.Classify("<p>Капітальний ремонт даху школи.</p>", 30));
    }

    [Fact]
    public void Without_threshold_short_text_is_filled()
    {
        Assert.Equal(TextState.Filled, CardText.Classify("Укриття"));
    }
}
