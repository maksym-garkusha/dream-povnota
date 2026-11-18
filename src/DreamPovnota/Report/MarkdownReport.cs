using System.Globalization;
using System.Text;
using DreamPovnota.Cards;
using DreamPovnota.Checks;
using DreamPovnota.Stats;

namespace DreamPovnota.Report;

/// <summary>
/// Звіт у Markdown з тими самими числами, що на сторінці, і з тим, що на сторінку
/// не виносимо: коди сторін, заглушки, дати в кодах проєктів.
/// </summary>
public static class MarkdownReport
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    // у таблицях областей і секторів показуємо не всі перевірки, а ті, де є що побачити
    private static readonly string[] KeyChecks = ["sector", "cost", "funding", "procurement", "fresh", "title-en"];

    public static async Task WriteAsync(Summary summary, string path, CancellationToken ct = default)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, Build(summary), new UTF8Encoding(false), ct);
    }

    public static string Build(Summary summary)
    {
        var md = new StringBuilder();
        Header(md, summary);
        Counts(md, summary);
        StageTable(md, summary);
        CheckTable(md, summary);
        ChecksByStage(md, summary);
        Seeking(md, summary);
        SliceTable(md, "Області", "Область", summary.Regions, KeyChecks,
            "Упорядковано за назвою. «Кілька областей» означає, що коди місця проєкту належать різним областям.");
        SliceTable(md, "Сектори", "Сектор", summary.Sectors, KeyChecks.Where(c => c != "sector").ToArray(),
            "Сектор це перша літера коду за класифікацією Світового банку (WB-ECO). Коди, яких немає в довіднику " +
            "(у даних це O, OB, OC), зібрано в «Інший код сектору»; «Не вказано» означає, що коду немає зовсім.");
        OrganisationSizes(md, summary);
        Codes(md, summary);
        Other(md, summary);
        return md.ToString();
    }

    private static void Header(StringBuilder md, Summary summary)
    {
        md.AppendLine("# Наповненість карток проєктів у DREAM");
        md.AppendLine();
        md.AppendLine($"Дата зрізу: {Date(summary.AsOf)}." +
            (summary.Sync is { } sync ? $" Картки забрано з API {Date(sync.Finished)}." : ""));
        md.AppendLine();
        md.AppendLine("Незалежний навчальний проєкт на відкритих даних DREAM. Не пов'язаний з державними органами, " +
            "це не офіційна статистика. Як рахується кожна перевірка, описано в " +
            "[README](https://github.com/maksym-garkusha/dream-povnota#як-рахується).");
        md.AppendLine();
    }

    private static void Counts(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Скільки карток");
        md.AppendLine();
        md.AppendLine("| | Проєктів |");
        md.AppendLine("|---|---:|");
        if (summary.Sync is { } sync) md.AppendLine($"| У стрічці API | {N(sync.ProjectsInFeed)} |");
        md.AppendLine($"| Розібрано, у кеші | {N(summary.InCache)} |");
        md.AppendLine($"| Відкриті, беруть участь у перевірках | {N(summary.Open.Count)} |");
        md.AppendLine($"| Закриті (скасовані, помилкові, дублікати, не реалізовані) | {N(summary.Closed)} |");
        md.AppendLine($"| Невідомий статус | {N(summary.Unknown)} |");
        if (summary.Sync is { } s)
        {
            md.AppendLine($"| Не вдалося розібрати | {N(s.UnparsedIds.Count)} |");
            md.AppendLine($"| Не вдалося завантажити | {N(s.ProjectsFailed)} |");
        }
        md.AppendLine();

        md.AppendLine($"Середня наповненість відкритих карток {P(summary.All.AverageScore)}, " +
            $"усе доречне заповнено в {N(summary.All.Complete)} з {N(summary.Open.Count)} ({P(Share(summary.All.Complete, summary.Open.Count))}). " +
            $"Організацій, що ведуть відкриті картки: {N(summary.Organisations.Count)}.");
        md.AppendLine();

        if (summary.CodeDates is var (first, last))
        {
            md.AppendLine($"Дати в кодах проєктів (DREAM-UA-ддммрр-...) від {first:dd.MM.yyyy} до {last:dd.MM.yyyy}.");
            md.AppendLine();
        }
    }

    private static void StageTable(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Стадії");
        md.AppendLine();
        md.AppendLine("| Стадія | Проєктів | Середня наповненість | Заповнено все доречне |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var stage in summary.Stages)
            md.AppendLine($"| {stage.Title} | {N(stage.Projects)} | {(stage.Projects > 0 ? P(stage.AverageScore) : "")} | {N(stage.Complete)} |");
        md.AppendLine();
    }

    private static void CheckTable(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Що заповнено");
        md.AppendLine();
        md.AppendLine("«Доречна» означає, у скількох відкритих картках це поле має сенс на їхній стадії. " +
            "Англійські поля в наповненість картки не входять.");
        md.AppendLine();
        md.AppendLine("| Група | Перевірка | Доречна | Заповнено | Частка | Не заповнено |");
        md.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var (check, tally) in CheckList.All.Zip(summary.All.Checks))
        {
            string title = check.Condition is null ? check.Title : $"{check.Title} ({check.Condition})";
            md.AppendLine($"| {check.Group.Title()} | {title} | {N(tally.Applicable)} | {N(tally.Filled)} | " +
                $"{(tally.Applicable > 0 ? P(tally.Share) : "")} | {N(tally.Applicable - tally.Filled)} |");
        }
        md.AppendLine();
    }

    private static void ChecksByStage(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Перевірки за стадіями");
        md.AppendLine();
        md.AppendLine("Заповнено з доречних. Порожня клітинка: на цій стадії перевірка не застосовується.");
        md.AppendLine();
        md.AppendLine("| Перевірка | " + string.Join(" | ", summary.Stages.Select(s => s.Title)) + " |");
        md.AppendLine("|---|" + string.Concat(summary.Stages.Select(_ => "---:|")));
        for (int i = 0; i < CheckList.All.Count; i++)
        {
            var cells = summary.Stages.Select(s => s.Checks[i] is { Applicable: > 0 } t ? $"{N(t.Filled)} з {N(t.Applicable)}" : "");
            md.AppendLine($"| {CheckList.All[i].Title} | {string.Join(" | ", cells)} |");
        }
        md.AppendLine();
    }

    private static void Seeking(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Шукають фінансування");
        md.AppendLine();
        md.AppendLine($"Схвалених проєктів без жодного джерела фінансування і без закупівель: {N(summary.SeekingFunding)}. " +
            $"Назву англійською мають {N(summary.SeekingFundingWithEnglishTitle)} з них.");
        md.AppendLine();
    }

    private static void SliceTable(
        StringBuilder md, string heading, string column, IReadOnlyList<Slice> slices, string[] checks, string note)
    {
        md.AppendLine($"## {heading}");
        md.AppendLine();
        md.AppendLine(note + " Частки за перевірками рахуються від карток, де перевірка доречна.");
        md.AppendLine();
        md.AppendLine($"| {column} | Проєктів | Наповненість | " +
            string.Join(" | ", checks.Select(c => CheckList.Get(c).Title)) + " |");
        md.AppendLine("|---|---:|---:|" + string.Concat(checks.Select(_ => "---:|")));
        foreach (var slice in slices)
        {
            var shares = checks.Select(c => slice[c] is { Applicable: > 0 } t ? P(t.Share) : "");
            md.AppendLine($"| {slice.Title} | {N(slice.Projects)} | {P(slice.AverageScore)} | {string.Join(" | ", shares)} |");
        }
        md.AppendLine();
    }

    private static void OrganisationSizes(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Організації");
        md.AppendLine();
        md.AppendLine("Організація це сторона з роллю projectAuthor, вона веде картку. Перелік організацій з їхніми " +
            "картками є на сторінці; тут тільки скільки в кого карток.");
        md.AppendLine();
        md.AppendLine("| Відкритих карток в організації | Організацій |");
        md.AppendLine("|---|---:|");
        (string Title, int Min, int Max)[] sizes = [("1", 1, 1), ("2–5", 2, 5), ("6–20", 6, 20), ("21–100", 21, 100), ("понад 100", 101, int.MaxValue)];
        foreach (var (title, min, max) in sizes)
            md.AppendLine($"| {title} | {N(summary.Organisations.Count(o => o.Slice.Projects >= min && o.Slice.Projects <= max))} |");
        md.AppendLine();
    }

    private static void Codes(StringBuilder md, Summary summary)
    {
        var codes = summary.Codes;
        md.AppendLine("## Коди сторін");
        md.AppendLine();
        md.AppendLine($"- Різних кодів ЄДРПОУ у відкритих картках: {N(codes.DistinctEdrpou)}, " +
            (codes.InvalidEdrpou.Count == 0
                ? "усі проходять перевірку контрольного розряду."
                : $"не проходять перевірку контрольного розряду {N(codes.InvalidEdrpou.Count)}: {string.Join(", ", codes.InvalidEdrpou)}."));
        md.AppendLine($"- Сторін, у яких під схемою UA-EDR стоїть десятизначний номер (податковий номер підприємця): " +
            $"{N(codes.TenDigitParties)} у {N(codes.TenDigitProjects)} картках. Самі номери програма не зберігає.");
        md.AppendLine();
    }

    private static void Other(StringBuilder md, Summary summary)
    {
        md.AppendLine("## Інше");
        md.AppendLine();
        md.AppendLine($"- Відкритих карток, де замість опису службове слово (`NULL`, дефіс, крапка): {N(summary.DescriptionStubs)}.");
        md.AppendLine($"- Відкритих карток, пов'язаних з об'єктами, яких немає в кеші (немає в стрічці об'єктів або не завантажились): {N(summary.ProjectsWithMissingObjects)}. " +
            "Місце й документи для них рахуються без цих об'єктів.");
        foreach (var (status, count) in summary.UnknownStatuses.OrderBy(s => s.Key, StringComparer.Ordinal))
            md.AppendLine($"- Статус `{status}`, якого немає в переліку стадій: {N(count)}.");
        md.AppendLine();
    }

    private static double Share(int part, int total) => total == 0 ? 0 : (double)part / total;

    private static string N(int value) => value.ToString("N0", Uk);

    private static string P(double share) => (share * 100).ToString("0.0", Uk) + "%";

    private static string Date(DateTimeOffset date) => date.UtcDateTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
