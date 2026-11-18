using MiniExcelLibs;

namespace DreamPovnota.Web.Lists;

/// <summary>
/// Аркуш «Картки» з рядками й цілими числами, без формул. MiniExcel пише потоково,
/// тож 15 тисяч рядків не тримаються в пам'яті цілою книгою.
/// </summary>
public static class XlsxExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string SheetName = "Картки";

    public static async Task WriteAsync(Stream stream, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, CancellationToken ct)
    {
        var sheet = rows.Select(row =>
        {
            var record = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);
            for (int i = 0; i < headers.Count; i++) record[headers[i]] = row[i];
            return record;
        });

        if (stream.CanSeek)
        {
            await stream.SaveAsAsync(sheet, sheetName: SheetName, excelType: ExcelType.XLSX, cancellationToken: ct);
            return;
        }

        // zip пишеться з переходами назад, тому у відповідь (вона не seekable) спершу через тимчасовий файл
        string path = Path.GetTempFileName();
        await using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await file.SaveAsAsync(sheet, sheetName: SheetName, excelType: ExcelType.XLSX, cancellationToken: ct);
        file.Position = 0;
        await file.CopyToAsync(stream, ct);
    }
}
