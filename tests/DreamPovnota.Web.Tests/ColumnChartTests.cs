using System.Xml.Linq;
using DreamPovnota.Web.Changes;

namespace DreamPovnota.Web.Tests;

public class ColumnChartTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static XElement Render(params int[] values) => XElement.Parse(ColumnChart.Render(
        "c1", "Оновлено карток за тижнями", "карток",
        values.Select((v, i) => new ChartColumn($"{16 + i}.11", v, i == values.Length - 1, $"Тиждень {i + 1}: {v}")).ToList()));

    /// <summary>
    /// Чи може SVG щось завантажити чи виконати: посилання, скрипти, вбудовані стилі,
    /// зовнішні шрифти чи картинки. Сторінка змін перевіряє цим самим кожен свій графік.
    /// </summary>
    internal static void AssertSelfContained(XElement svg)
    {
        string[] forbidden = ["script", "style", "image", "foreignObject", "use", "a", "iframe", "link"];
        Assert.DoesNotContain(svg.DescendantsAndSelf(), e => forbidden.Contains(e.Name.LocalName));
        foreach (var attribute in svg.DescendantsAndSelf().Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            Assert.NotEqual("style", attribute.Name.LocalName);
            Assert.NotEqual("href", attribute.Name.LocalName);
            Assert.NotEqual("src", attribute.Name.LocalName);
            Assert.False(attribute.Name.LocalName.StartsWith("on", StringComparison.Ordinal), attribute.Name.LocalName);
            Assert.DoesNotContain("url(", attribute.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", attribute.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Chart_has_no_external_resources_and_names_itself()
    {
        var svg = Render(3, 0, 12);

        AssertSelfContained(svg);
        Assert.Equal("img", (string?)svg.Attribute("role"));
        Assert.Equal("c1", (string?)svg.Attribute("aria-labelledby"));
        Assert.Equal("Оновлено карток за тижнями", svg.Element(Svg + "title")!.Value);
    }

    [Fact]
    public void Each_column_has_a_tooltip_and_a_zero_draws_no_bar()
    {
        var svg = Render(3, 0, 12);
        var columns = svg.Elements(Svg + "g").ToList();

        Assert.Equal(["Тиждень 1: 3", "Тиждень 2: 0", "Тиждень 3: 12"], columns.Select(g => g.Element(Svg + "title")!.Value));
        Assert.Equal([1, 0, 1], columns.Select(g => g.Elements(Svg + "path").Count()));
        Assert.Equal(["3", "0", "12"], columns.Select(g => g.Elements(Svg + "text").Single().Value));
        Assert.Equal("col partial", (string?)columns[2].Element(Svg + "path")!.Attribute("class"));
    }

    [Fact]
    public void Axis_has_round_ticks_and_the_unit()
    {
        var svg = Render(3, 0, 12);
        var texts = svg.Elements(Svg + "text").ToList();

        Assert.Equal("карток", texts.Single(t => (string?)t.Attribute("class") == "unit").Value);
        Assert.Equal(["0", "5", "10", "15"], texts.Where(t => (string?)t.Attribute("class") == "tick").Select(t => t.Value));
        Assert.Equal(["16.11", "17.11", "18.11"], texts.Where(t => (string?)t.Attribute("class") == "label").Select(t => t.Value));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(7, 8, 2)]
    [InlineData(12, 15, 5)]
    [InlineData(40, 40, 10)]
    [InlineData(1234, 1500, 500)]
    public void Scale_rounds_the_top_to_a_whole_step(int max, int top, int step) =>
        Assert.Equal((top, step), ColumnChart.Scale(max));

    [Fact]
    public void Narrow_columns_label_every_other_date_ending_with_the_latest_and_drop_values()
    {
        var svg = Render(Enumerable.Range(1, 12).ToArray());
        var labels = svg.Elements(Svg + "text").Where(t => (string?)t.Attribute("class") == "label").Select(t => t.Value).ToList();

        Assert.Equal(6, labels.Count);
        Assert.Equal("27.11", labels[^1]);
        Assert.DoesNotContain(svg.Descendants(Svg + "text"), t => (string?)t.Attribute("class") == "val");
    }

    [Fact]
    public void Text_from_data_is_escaped()
    {
        string svg = ColumnChart.Render("c2", "<script>", "полів", [new ChartColumn("<b>", 1, false, "\"&<")]);

        Assert.DoesNotContain("<script>", svg);
        Assert.DoesNotContain("<b>", svg);
        AssertSelfContained(XElement.Parse(svg));
    }
}
