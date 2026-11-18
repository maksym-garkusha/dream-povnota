using System.Globalization;
using System.Xml.Linq;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Changes;

/// <summary>Стовпчик: підпис під віссю, значення, неповний період і текст підказки.</summary>
/// <param name="Value">Null, якщо за період порівнювати не було з чим: тоді ні стовпчика, ні нуля над ним.</param>
public sealed record ChartColumn(string Label, int? Value, bool Partial, string Tooltip);

/// <summary>
/// Стовпчикова діаграма SVG, зібрана на сервері. Без скриптів, посилань і атрибута style:
/// CSP сервісу не пускає вбудованих стилів, тож кольори й шрифт задає site.css за класами.
/// Підказку до стовпчика показує сам браузер з елемента title, а ті самі числа є в таблиці
/// під графіком. Розмір у viewBox розраховано на ширину телефона: на широкому екрані
/// графік просто більшає разом з текстом.
/// </summary>
public static class ColumnChart
{
    public const int Width = 360;
    public const int Height = 186;

    private const double Left = 36, Right = 354, Top = 22, Bottom = 152;
    private const double MaxBar = 24, Corner = 4;

    // підпис дати «16.11» займає близько 32 одиниць; вужчим стовпчикам підписуємо кожен другий чи третій
    private const double LabelWidth = 36;

    // значення над кожним стовпчиком, поки їх небагато; далі вони б налазили одне на одне
    private const int MaxValueLabels = 8;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <param name="id">Унікальний на сторінці id для назви діаграми.</param>
    /// <param name="unit">Одиниця осі значень: «карток», «полів».</param>
    public static string Render(string id, string title, string unit, IReadOnlyList<ChartColumn> columns)
    {
        int max = columns.Count == 0 ? 0 : columns.Max(c => c.Value ?? 0);
        var (top, step) = Scale(max);
        double plotHeight = Bottom - Top;
        double slot = (Right - Left) / Math.Max(columns.Count, 1);
        double bar = Math.Min(MaxBar, slot * 0.6);
        int every = (int)Math.Ceiling(LabelWidth / slot);

        var root = new XElement(Svg + "svg",
            new XAttribute("class", "chart"),
            new XAttribute("viewBox", $"0 0 {Width} {Height}"),
            new XAttribute("role", "img"),
            new XAttribute("aria-labelledby", id),
            new XElement(Svg + "title", new XAttribute("id", id), title),
            Text("unit", 0, 11, unit, "start"));

        for (int tick = 0; tick <= top; tick += step)
        {
            double y = Bottom - plotHeight * tick / top;
            if (tick > 0) root.Add(Line("grid", Left, y, Right, y));
            root.Add(Text("tick", Left - 6, y + 4, Format.Number(tick), "end"));
        }
        root.Add(Line("base", Left, Bottom, Right, Bottom));

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            double centre = Left + slot * (i + 0.5);
            double height = plotHeight * (column.Value ?? 0) / top;
            var group = new XElement(Svg + "g",
                new XAttribute("class", "column"),
                new XElement(Svg + "title", column.Tooltip),
                new XElement(Svg + "rect",
                    new XAttribute("class", "hit"),
                    new XAttribute("x", N(Left + slot * i)), new XAttribute("y", N(Top)),
                    new XAttribute("width", N(slot)), new XAttribute("height", N(Bottom - Top))));
            if (height > 0)
                group.Add(new XElement(Svg + "path",
                    new XAttribute("class", column.Partial ? "col partial" : "col"),
                    new XAttribute("d", Bar(centre - bar / 2, Bottom - height, bar, height))));
            if (columns.Count <= MaxValueLabels && column.Value is { } value)
                group.Add(Text("val", centre, Bottom - height - 5, Format.Number(value), "middle"));
            root.Add(group);

            // підписуємо від останнього стовпчика назад, щоб найсвіжіший період мав підпис завжди
            if ((columns.Count - 1 - i) % every == 0)
                root.Add(Text("label", centre, Bottom + 17, column.Label, "middle"));
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Верх осі й крок поділок: 1, 2 або 5 на степінь десяти, не більше чотирьох поділок над нулем.</summary>
    public static (int Top, int Step) Scale(int max)
    {
        if (max <= 0) return (1, 1);
        double rough = max / 4.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double step = new[] { 1, 2, 5, 10 }.Select(m => m * magnitude).First(s => s >= rough);
        int whole = Math.Max(1, (int)Math.Round(step));
        return ((int)Math.Ceiling((double)max / whole) * whole, whole);
    }

    // заокруглений верх і прямий низ: стовпчик росте від осі
    private static string Bar(double x, double y, double width, double height)
    {
        double r = Math.Min(Corner, Math.Min(width / 2, height));
        return $"M{N(x)},{N(y + height)}V{N(y + r)}A{N(r)},{N(r)} 0 0 1 {N(x + r)},{N(y)}" +
               $"H{N(x + width - r)}A{N(r)},{N(r)} 0 0 1 {N(x + width)},{N(y + r)}V{N(y + height)}Z";
    }

    private static XElement Line(string css, double x1, double y1, double x2, double y2) =>
        new(Svg + "line",
            new XAttribute("class", css),
            new XAttribute("x1", N(x1)), new XAttribute("y1", N(y1)),
            new XAttribute("x2", N(x2)), new XAttribute("y2", N(y2)));

    private static XElement Text(string css, double x, double y, string text, string anchor) =>
        new(Svg + "text",
            new XAttribute("class", css),
            new XAttribute("x", N(x)), new XAttribute("y", N(y)),
            new XAttribute("text-anchor", anchor),
            text);

    private static string N(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
