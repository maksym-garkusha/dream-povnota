using System.Globalization;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace DreamPovnota.Digest;

/// <summary>
/// Одна сторінка A4: два числа й дата останньої зміни, до п'яти полів, які найпростіше дописати,
/// зміни за 7 днів і посилання.
/// Шрифт IBM Plex Sans (SIL OFL 1.1) лежить поруч у Fonts і вбудовується у файл,
/// тож кирилиця однакова на будь-якому комп'ютері. Назви карток сюди не потрапляють:
/// весь текст наш, і для нього в шрифті є всі знаки.
/// </summary>
public static class DigestPdf
{
    public const string ContentType = "application/pdf";

    private const double Margin = 56;
    private const double Width = 595 - 2 * Margin;

    private static readonly (byte R, byte G, byte B) Ink = (0x14, 0x20, 0x1c);
    private static readonly (byte R, byte G, byte B) Muted = (0x5e, 0x6b, 0x66);
    private static readonly (byte R, byte G, byte B) Blue = (0x1f, 0x4f, 0xd8);
    private static readonly (byte R, byte G, byte B) Line = (0xd5, 0xdb, 0xd8);

    private static readonly Lazy<byte[]> RegularFont = new(() => Font("IBMPlexSans-Regular.ttf"));
    private static readonly Lazy<byte[]> BoldFont = new(() => Font("IBMPlexSans-Bold.ttf"));

    public static byte[] Write(OrganisationDigest digest)
    {
        using var builder = new PdfDocumentBuilder();
        var info = builder.DocumentInformation;
        info.Title = "Зріз наповненості карток DREAM: " + digest.Organisation.Title;
        info.Author = DigestWorkbook.Author;
        info.Subject = "Відкриті картки організації на " + DigestText.Date(digest.Generated);
        // типово тут назва бібліотеки; пишемо назву проєкту
        info.Creator = "dream-povnota";
        info.Producer = "dream-povnota";
        info.CreationDate = PdfDate(digest.Generated);
        info.ModifiedDate = info.CreationDate;

        var page = new Page(builder.AddPage(PageSize.A4), builder.AddTrueTypeFont(RegularFont.Value), builder.AddTrueTypeFont(BoldFont.Value));
        Draw(page, digest);
        return builder.Build();
    }

    private static void Draw(Page p, OrganisationDigest d)
    {
        p.Text($"Незалежний навчальний проєкт на відкритих даних DREAM, не офіційна статистика. Випуск даних {DigestText.Date(d.Generated)}.", 9.5, Muted);
        p.Gap(10);
        p.Text(d.Organisation.Title, 19, Ink, bold: true, leading: 1.2);
        p.Text("ЄДРПОУ " + d.Organisation.Edrpou, 10, Muted);
        p.Gap(18);

        p.Figures(
            (DigestText.Number(d.Open), DigestText.Plural(d.Open, "відкрита картка", "відкриті картки", "відкритих карток")),
            (DigestText.Number(d.Complete), "заповнено все доречне"),
            (d.LastUpdated is { } last ? DigestText.Date(last) : "немає", "остання зміна картки в DREAM"));
        p.Rule();

        p.Text("Що найпростіше дозаповнити", 13, Ink, bold: true);
        p.Gap(4);
        if (d.Open == 0)
            p.Text("Відкритих карток організації в цьому випуску немає.", 10.5, Ink);
        else if (d.Easiest.Count == 0)
            p.Text("У всіх відкритих картках заповнено все доречне, разом з англійськими полями.", 10.5, Ink);
        for (int i = 0; i < d.Easiest.Count; i++)
        {
            var item = d.Easiest[i];
            p.Text($"{i + 1}. {item.Title}", 11, Ink, bold: true);
            p.Text($"{DigestEase.Gap(item.Check, item.Cards, d.Open)} Що потрібно: {item.Hint}.", 10, Muted, indent: 14);
            p.Gap(3);
        }
        p.Gap(8);
        p.Rule();

        p.Text($"За 7 днів, {d.WeekTitle}", 13, Ink, bold: true);
        p.Gap(4);
        p.Text(d.RecentSentence, 10.5, Ink);
        p.Text(d.HistoryNote, 9.5, Muted);
        p.Gap(14);

        p.Text("Сторінка організації з усіма картками:", 10.5, Ink);
        p.Link(d.OrganisationPage, 10.5);
        p.Text("Сторінка зрізу з таблицею xlsx:", 10.5, Ink);
        p.Link(d.DigestPage, 10.5);

        p.Footer(
            "Незалежний навчальний проєкт на відкритих даних, не пов'язаний з державними органами, не офіційна статистика. " +
            "Дані: публічний API DREAM (dream.gov.ua), ліцензія CC BY 4.0. «Заповнено все доречне» рахується без англійських полів, як на сторінці організації. " +
            $"Випуск даних {DigestText.DateTime(d.Generated)}, SHA-256 файлу data.json починається з {d.EditionHashPrefix}; " +
            "повний хеш є в /api/v1/meta сервісу, а походження data.json підтверджує атестація GitHub.");
    }

    private static string PdfDate(DateTimeOffset moment) =>
        "D:" + moment.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static byte[] Font(string name)
    {
        using var stream = typeof(DigestPdf).Assembly.GetManifestResourceStream("DreamPovnota.Digest.Fonts." + name)
            ?? throw new InvalidOperationException($"Шрифту {name} немає серед ресурсів збірки");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Сторінка з курсором зверху вниз; PDF рахує висоту від низу.</summary>
    private sealed class Page(PdfPageBuilder page, PdfDocumentBuilder.AddedFont regular, PdfDocumentBuilder.AddedFont heavy)
    {
        private double y = 842 - Margin;

        public void Gap(double points) => y -= points;

        public void Text(string text, double size, (byte R, byte G, byte B) color, bool bold = false, double leading = 1.35, double indent = 0)
        {
            var font = bold ? heavy : regular;
            page.SetTextAndFillColor(color.R, color.G, color.B);
            foreach (string line in Wrap(text, size, font, Width - indent))
            {
                y -= size * leading;
                page.AddText(line, size, new PdfPoint(Margin + indent, y), font);
            }
            page.ResetColor();
        }

        public void Link(string url, double size)
        {
            y -= size * 1.35;
            page.SetTextAndFillColor(Blue.R, Blue.G, Blue.B);
            var letters = page.AddText(url, size, new PdfPoint(Margin, y), regular);
            page.ResetColor();
            // без рамки: типова рамка посилання в переглядачах виглядає як поле форми
            var area = new PdfRectangle(Margin, y - 3, Margin + TextWidth(letters), y + size);
            page.AddLink(new LinkAnnotation(new UriAction(url), area, new AnnotationBorder(0, 0, 0, null), null, null, []));
            y -= 4;
        }

        /// <summary>Три великі значення в ряд з підписами під ними; задовге значення, як дата, дрібнішає, щоб уміститися в колонку.</summary>
        public void Figures(params (string Value, string Caption)[] figures)
        {
            double column = Width / figures.Length;
            double top = y;
            double lowest = y;
            for (int i = 0; i < figures.Length; i++)
            {
                double x = Margin + i * column;
                double line = top - 30;
                double size = Math.Min(30, 30 * (column - 14) / Math.Max(Measure(figures[i].Value, 30, heavy), 1));
                page.AddText(figures[i].Value, size, new PdfPoint(x, line), heavy);
                page.SetTextAndFillColor(Muted.R, Muted.G, Muted.B);
                foreach (string caption in Wrap(figures[i].Caption, 10, regular, column - 14))
                {
                    line -= 13.5;
                    page.AddText(caption, 10, new PdfPoint(x, line), regular);
                }
                page.ResetColor();
                lowest = Math.Min(lowest, line);
            }
            y = lowest - 16;
        }

        public void Rule()
        {
            page.SetStrokeColor(Line.R, Line.G, Line.B);
            page.DrawLine(new PdfPoint(Margin, y), new PdfPoint(Margin + Width, y), 0.75);
            page.ResetColor();
            y -= 16;
        }

        /// <summary>Дрібний текст унизу сторінки, незалежно від того, де закінчився зміст.</summary>
        public void Footer(string text)
        {
            var lines = Wrap(text, 8.5, regular, Width);
            double line = Margin + lines.Count * 11.5;
            page.SetTextAndFillColor(Muted.R, Muted.G, Muted.B);
            foreach (string part in lines)
            {
                line -= 11.5;
                page.AddText(part, 8.5, new PdfPoint(Margin, line), regular);
            }
            page.ResetColor();
        }

        private List<string> Wrap(string text, double size, PdfDocumentBuilder.AddedFont font, double width)
        {
            var lines = new List<string>();
            string current = "";
            foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Measure(candidate, size, font) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        private double Measure(string text, double size, PdfDocumentBuilder.AddedFont font) =>
            TextWidth(page.MeasureText(text, size, new PdfPoint(0, 0), font));

        private static double TextWidth(IReadOnlyList<Letter> letters) =>
            letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
    }
}
