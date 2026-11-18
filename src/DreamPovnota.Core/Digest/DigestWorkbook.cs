using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace DreamPovnota.Digest;

/// <summary>
/// Книга зрізу: «Зріз» з числами, «Картки» і «Що змінилось за тиждень».
/// Файл невеликий (сотні рядків), тож збирається в пам'яті.
/// </summary>
public static class DigestWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Author = "Максим Гаркуша";

    public const string SummarySheet = "Зріз";
    public const string CardsSheet = "Картки";
    public const string WeekSheet = "Що змінилось за тиждень";

    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static async Task WriteAsync(Stream output, OrganisationDigest digest, CancellationToken ct)
    {
        var sheets = new Dictionary<string, object>
        {
            [SummarySheet] = SummaryRows(digest),
            [CardsSheet] = CardRows(digest),
            [WeekSheet] = WeekRows(digest),
        };
        // ширину колонок за вмістом MiniExcel рахує лише у FastMode; для сотень рядків пам'яті це не коштує
        var configuration = new OpenXmlConfiguration
        {
            FastMode = true,
            EnableAutoWidth = true,
            MaxWidth = 70,
            FreezeRowCount = 1,
        };

        using var buffer = new MemoryStream();
        await buffer.SaveAsAsync(sheets, excelType: ExcelType.XLSX, configuration: configuration, cancellationToken: ct);
        AddProperties(buffer, digest);
        buffer.Position = 0;
        await buffer.CopyToAsync(output, ct);
    }

    private static List<Dictionary<string, object?>> SummaryRows(OrganisationDigest d)
    {
        var rows = new List<(string, object?)>
        {
            ("Організація", d.Organisation.Title),
            ("ЄДРПОУ", d.Organisation.Edrpou),
            ("Випуск даних", DigestText.DateTime(d.Generated)),
            ("SHA-256 data.json, перші 16 знаків", d.EditionHashPrefix),
            ("Відкритих карток", d.Open),
            ("Заповнено все доречне", d.Complete),
            ($"Оновлено за тиждень, {d.WeekTitle}", d.UpdatedThisWeek),
            ("Історія змін", d.HistoryNote),
            ("Сторінка організації", d.OrganisationPage),
            ("Джерело", "Публічний API DREAM (dream.gov.ua), ліцензія даних CC BY 4.0"),
            ("Примітка", "Незалежний навчальний проєкт на відкритих даних, не пов'язаний з державними органами, не офіційна статистика. Англійські поля в «заповнено все доречне» не входять."),
        };
        return rows.Select(r => Row(("Показник", r.Item1), ("Значення", r.Item2))).ToList();
    }

    private static List<Dictionary<string, object?>> CardRows(OrganisationDigest d) =>
        d.Cards.Select(c => Row(
            ("Код DREAM", "DREAM-UA-" + c.Code),
            ("Посилання на картку", c.Link),
            ("Назва", c.Title),
            ("Область", c.Region),
            ("Стадія", c.Stage),
            ("Сектор", c.Sector),
            ("Оновлено в DREAM", Iso(c.Updated)),
            ("Заповнено", c.Filled),
            ("Доречно", c.Applicable),
            ("Наповненість, %", c.Percent),
            ("Не заповнено", string.Join(", ", c.Missing)),
            ("Що дописати першим", c.FirstToAdd ?? "усе заповнено"))).ToList();

    private static List<Dictionary<string, object?>> WeekRows(OrganisationDigest d)
    {
        var rows = d.Week.Select(c => WeekRow(
            "DREAM-UA-" + c.Code, c.Link, c.Title, c.KindTitle, Iso(c.Updated), c.StageFrom, c.StageTo,
            string.Join(", ", c.NowFilled), string.Join(", ", c.NoLongerFilled))).ToList();

        // таблиця без жодного рядка вийшла б без заголовків, а пояснення про історію
        // має бути видно на цьому аркуші, а не тільки на першому
        if (rows.Count == 0) rows.Add(WeekRow(null, null, $"Змін за тиждень {d.WeekTitle} немає.", null, null, null, null, null, null));
        if (d.History != HistoryCoverage.FullWeek) rows.Add(WeekRow(null, null, d.HistoryNote, null, null, null, null, null, null));
        return rows;
    }

    private static Dictionary<string, object?> WeekRow(
        string? code, string? link, string title, string? kind, string? updated,
        string? stageFrom, string? stageTo, string? nowFilled, string? noLonger) => Row(
        ("Код DREAM", code),
        ("Посилання на картку", link),
        ("Назва", title),
        ("Що сталося", kind),
        ("Оновлено в DREAM", updated),
        ("Стадія на початок тижня", stageFrom),
        ("Стадія тепер", stageTo),
        ("Дозаповнено", nowFilled),
        ("Стало незаповненим", noLonger));

    private static Dictionary<string, object?> Row(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.Ordinal);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// MiniExcel не пише docProps, тож автора й назву дописуємо в пакет самі. Час частин
    /// пакета ставимо на дату випуску даних, а не на мить збірки.
    /// </summary>
    private static void AddProperties(MemoryStream buffer, OrganisationDigest digest)
    {
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true);
        string created = digest.Generated.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var core = new XDocument(new XElement(Cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", Cp),
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(XNamespace.Xmlns + "dcterms", DcTerms),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XElement(Dc + "title", "Зріз наповненості карток DREAM: " + digest.Organisation.Title),
            new XElement(Dc + "creator", Author),
            new XElement(Cp + "lastModifiedBy", Author),
            new XElement(DcTerms + "created", new XAttribute(Xsi + "type", "dcterms:W3CDTF"), created),
            new XElement(DcTerms + "modified", new XAttribute(Xsi + "type", "dcterms:W3CDTF"), created)));
        Write(zip, "docProps/core.xml", core);

        Update(zip, "[Content_Types].xml", types => types.Root!.Add(new XElement(ContentTypes + "Override",
            new XAttribute("PartName", "/docProps/core.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml"))));
        Update(zip, "_rels/.rels", rels => rels.Root!.Add(new XElement(Relationships + "Relationship",
            new XAttribute("Id", "rIdCoreProperties"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
            new XAttribute("Target", "docProps/core.xml"))));

        foreach (var entry in zip.Entries) entry.LastWriteTime = digest.Generated;
    }

    private static void Update(ZipArchive zip, string name, Action<XDocument> change)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"У книзі немає {name}");
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        change(document);
        entry.Delete();
        Write(zip, name, document);
    }

    private static void Write(ZipArchive zip, string name, XDocument document)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
