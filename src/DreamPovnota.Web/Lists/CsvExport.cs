using System.Globalization;
using System.Text;

namespace DreamPovnota.Web.Lists;

/// <summary>
/// CSV, який українська Excel відкриває подвійним клацанням: UTF-8 з BOM,
/// крапка з комою, CRLF. Рядка sep= немає: з ним Excel ігнорує BOM і ламає кирилицю.
/// </summary>
public static class CsvExport
{
    public const string ContentType = "text/csv; charset=utf-8";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task WriteAsync(Stream stream, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, Utf8WithBom, bufferSize: 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(Line(headers));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Line(row));
        }
        await writer.FlushAsync(ct);
    }

    private static string Line(IEnumerable<object?> cells) => string.Join(';', cells.Select(Cell));

    public static string Cell(object? value) => value switch
    {
        null => "",
        int n => n.ToString(CultureInfo.InvariantCulture),
        string s => Quote(Guard(s)),
        _ => throw new ArgumentException($"Непідтримуваний тип клітинки {value.GetType().Name}", nameof(value)),
    };

    // клітинку, що починається з = + - @, табуляції чи CR, Excel може виконати як формулу
    private static string Guard(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}
