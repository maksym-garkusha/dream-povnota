using System.Text;
using DreamPovnota.Web.Lists;

namespace DreamPovnota.Web.Tests;

public class CsvExportTests
{
    private static async Task<byte[]> Write(params object?[][] rows)
    {
        using var stream = new MemoryStream();
        await CsvExport.WriteAsync(stream, ["Код", "Назва"], rows, CancellationToken.None);
        return stream.ToArray();
    }

    [Fact]
    public async Task File_starts_with_a_byte_order_mark_and_uses_semicolons_and_crlf()
    {
        byte[] bytes = await Write(["DREAM-UA-1", 7]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Код;Назва\r\nDREAM-UA-1;7\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("Школа; корпус 2", "\"Школа; корпус 2\"")]
    [InlineData("Школа \"Надія\"", "\"Школа \"\"Надія\"\"\"")]
    [InlineData("два\nрядки", "\"два\nрядки\"")]
    public void Delimiters_quotes_and_line_breaks_are_quoted(string value, string expected)
    {
        Assert.Equal(expected, CsvExport.Cell(value));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+380")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tтаб")]
    public void Cell_that_Excel_could_run_as_a_formula_gets_an_apostrophe(string value)
    {
        Assert.StartsWith("'", CsvExport.Cell(value).TrimStart('"'));
    }

    [Fact]
    public void Numbers_are_written_without_separators_and_empty_cells_stay_empty()
    {
        Assert.Equal("14610", CsvExport.Cell(14610));
        Assert.Equal("", CsvExport.Cell(null));
    }
}
