using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DreamPovnota.Digest;
using DreamPovnota.Export;
using UglyToad.PdfPig;

namespace DreamPovnota.Tests;

/// <summary>
/// Зріз для агентства й служб відновлення. Фікстура digest-edition.json це справжній
/// data.json від 18.11.2026, обрізаний до карток агентства і служб у Вінницькій
/// та Черкаській областях. Еталонні числа перераховано з неї окремо, скриптом на Python.
/// </summary>
public partial class DigestTests
{
    private const string Agency = "37641918";
    private const string Vinnytsia = "25845655";
    private const string Cherkasy = "05422303";

    private static async Task<Edition> EditionAsync()
    {
        await using var file = File.OpenRead(Fixture.Path("digest-edition.json"));
        return await Edition.ReadAsync(file);
    }

    private static OrganisationDigest Build(Edition edition, string edrpou, DigestBaseline? baseline = null) =>
        DigestBuilder.Build(edition, DigestOrganisations.Find(edrpou)!, baseline);

    /// <summary>Стан на початок тижня, такий самий, як тепер, з тими самими бітами.</summary>
    private static DigestBaseline SameAsNow(Edition edition, DateTimeOffset historyFrom) => new(
        historyFrom,
        historyFrom,
        edition.Checks.Select(c => c.Code).ToList(),
        edition.Cards.Select(c => new BaselineCard(
            c.Code, c.Title, edition.Organisations[c.Organisation].Edrpou, edition.Stages[c.Stage].Key,
            c.Updated, c.Applicable, c.Filled)).ToList());

    private static int Bit(Edition edition, string code) => 1 << edition.Checks.ToList().FindIndex(c => c.Code == code);

    [Fact]
    public void List_holds_the_agency_and_twenty_one_regional_services_with_valid_unique_codes()
    {
        var all = DigestOrganisations.All;

        Assert.Equal(22, all.Count);
        Assert.Equal(Agency, all[0].Edrpou);
        Assert.Equal(21, all.Count(o => o.Title.StartsWith("Служба відновлення та розвитку інфраструктури", StringComparison.Ordinal)));
        Assert.All(all, o => Assert.Matches("^[0-9]{8}$", o.Edrpou));
        Assert.Equal(all.Count, all.Select(o => o.Edrpou).Distinct().Count());
        Assert.All(all, o => Assert.True(DigestOrganisations.LooksLikeRestorationBody(o.Title)));
    }

    [Fact]
    public async Task Every_restoration_body_in_the_data_is_on_the_list()
    {
        var edition = await EditionAsync();

        var unlisted = DigestOrganisations.Unlisted(edition.Organisations.Select(o => (o.Edrpou, o.Title)));

        Assert.Empty(unlisted);
        Assert.Single(DigestOrganisations.Unlisted([("12345678", "СЛУЖБА ВІДНОВЛЕННЯ ТА РОЗВИТКУ ІНФРАСТРУКТУРИ У ВОЛИНСЬКІЙ ОБЛАСТІ")]));
        Assert.Empty(DigestOrganisations.Unlisted([("12345678", "Служба автомобільних доріг у Волинській області")]));
    }

    [Theory]
    [InlineData(Agency, 9, 4)]
    [InlineData(Vinnytsia, 16, 14)]
    [InlineData(Cherkasy, 1, 0)]
    [InlineData("26345736", 0, 0)]
    public async Task Counts_match_the_reference_numbers(string edrpou, int open, int complete)
    {
        var digest = Build(await EditionAsync(), edrpou);

        Assert.Equal(open, digest.Open);
        Assert.Equal(complete, digest.Complete);
        Assert.Equal(0, digest.UpdatedThisWeek);
        Assert.Equal(new DateOnly(2026, 11, 12), digest.WeekFrom);
        Assert.Equal(new DateOnly(2026, 11, 18), digest.WeekTo);
    }

    [Fact]
    public async Task Cards_updated_within_the_seven_days_count_as_updated_this_week()
    {
        var edition = await EditionAsync();
        var cards = edition.Cards.ToList();
        int first = cards.FindIndex(c => edition.Organisations[c.Organisation].Edrpou == Vinnytsia);
        cards[first] = cards[first] with { Updated = new DateOnly(2026, 11, 12) };
        cards[first + 1] = cards[first + 1] with { Updated = new DateOnly(2026, 11, 11) };

        var digest = Build(edition with { Cards = cards }, Vinnytsia);

        Assert.Equal(1, digest.UpdatedThisWeek);
        var row = Assert.Single(digest.Week);
        Assert.Equal(cards[first].Code, row.Code);
        Assert.Equal(WeekChangeKind.Updated, row.Kind);
    }

    [Fact]
    public async Task Easiest_fields_go_from_a_list_choice_to_texts_and_stop_at_five()
    {
        var edition = await EditionAsync();

        var vinnytsia = Build(edition, Vinnytsia).Easiest;
        var cherkasy = Build(edition, Cherkasy).Easiest;
        var agency = Build(edition, Agency).Easiest;

        Assert.Equal(["sector", "title-en", "duration", "description-en", "fresh"], vinnytsia.Select(e => e.Check));
        Assert.Equal([1, 16, 1, 16, 2], vinnytsia.Select(e => e.Cards));
        Assert.Equal(["sector", "title-en", "duration", "purpose", "description-en"], cherkasy.Select(e => e.Check));
        Assert.Equal(["title-en", "description-en", "fresh", "funding"], agency.Select(e => e.Check));
        Assert.Equal("вибір зі списку", vinnytsia[0].Hint);
    }

    [Fact]
    public async Task First_to_add_is_the_easiest_scored_field_and_english_only_when_the_rest_is_filled()
    {
        var edition = await EditionAsync();

        var cherkasy = Assert.Single(Build(edition, Cherkasy).Cards);
        var vinnytsia = Build(edition, Vinnytsia).Cards;

        // у картці Черкаської служби бракує й сектору, й англійської назви: сектор входить у наповненість
        Assert.Equal("Сектор", cherkasy.FirstToAdd);
        Assert.Contains("Назва англійською", cherkasy.Missing);
        Assert.Equal(14, vinnytsia.Count(c => c.FirstToAdd == "Назва англійською"));
        Assert.All(vinnytsia.Where(c => c.Percent == 100), c => Assert.Equal("Назва англійською", c.FirstToAdd));
    }

    [Fact]
    public async Task Without_history_the_digest_says_so_and_lists_no_filled_fields()
    {
        var digest = Build(await EditionAsync(), Vinnytsia);

        Assert.Equal(HistoryCoverage.Unavailable, digest.History);
        Assert.Empty(digest.Week);
        Assert.Contains("недоступна", digest.HistoryNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_younger_than_a_week_is_stated_with_its_start_date()
    {
        var edition = await EditionAsync();
        var start = DateTimeOffset.Parse("2026-11-18T01:12:54Z");

        var digest = Build(edition, Vinnytsia, SameAsNow(edition, start));

        Assert.Equal(HistoryCoverage.Partial, digest.History);
        Assert.Empty(digest.Week);
        Assert.Equal("Історія ведеться з 18.11.2026, тож дозаповнені поля видно лише від цієї дати.", digest.HistoryNote);
    }

    [Fact]
    public async Task Week_lists_filled_fields_stage_changes_new_and_gone_cards()
    {
        var edition = await EditionAsync();
        var baseline = SameAsNow(edition, DateTimeOffset.Parse("2026-11-01T05:00:00Z"));
        var old = baseline.Cards.ToList();
        var mine = old.Select((c, i) => (c, i)).Where(x => x.c.Edrpou == Cherkasy || x.c.Edrpou == Vinnytsia).ToList();

        // картка Черкаської служби тиждень тому була без документів і на стадії ідеї
        var (cherkasy, ci) = mine.First(x => x.c.Edrpou == Cherkasy);
        old[ci] = cherkasy with { Filled = cherkasy.Filled & ~Bit(edition, "documents"), Stage = "idea" };
        // одна картка Вінницької служби тоді ще не існувала, а інша тоді була, а тепер закрита
        var (fresh, fi) = mine.First(x => x.c.Edrpou == Vinnytsia);
        old.RemoveAt(fi);
        old.Add(new BaselineCard("010125-AAAAAAAA", "Ремонт мосту, якого вже немає серед відкритих", Vinnytsia, "defined", new DateOnly(2025, 1, 1), 63, 63));

        var vinnytsia = Build(edition, Vinnytsia, baseline with { Cards = old });
        var cherkasyDigest = Build(edition, Cherkasy, baseline with { Cards = old });

        Assert.Equal(HistoryCoverage.FullWeek, vinnytsia.History);
        Assert.Equal([WeekChangeKind.Appeared, WeekChangeKind.Left], vinnytsia.Week.Select(c => c.Kind));
        Assert.Equal(fresh.Code, vinnytsia.Week[0].Code);
        Assert.Equal("010125-AAAAAAAA", vinnytsia.Week[1].Code);

        var change = Assert.Single(cherkasyDigest.Week);
        Assert.Equal(WeekChangeKind.Updated, change.Kind);
        Assert.Equal(["Документи або фото"], change.NowFilled);
        Assert.Equal(("Ідея", "Реалізація"), (change.StageFrom, change.StageTo));
        Assert.Equal(1, cherkasyDigest.FieldsFilledThisWeek);
        Assert.Equal("Порівняння зі станом карток на 01.11.2026.", cherkasyDigest.HistoryNote);
    }

    [Fact]
    public async Task Week_compares_fields_by_code_when_the_check_order_changed()
    {
        var edition = await EditionAsync();
        var baseline = SameAsNow(edition, DateTimeOffset.Parse("2026-11-01T05:00:00Z"));
        // у знімку перевірки в зворотному порядку: ті самі поля, інші біти
        var codes = baseline.Checks.Reverse().ToList();
        int Reverse(int mask) => Enumerable.Range(0, codes.Count).Where(i => (mask & (1 << i)) != 0)
            .Aggregate(0, (m, i) => m | 1 << (codes.Count - 1 - i));
        var reversed = baseline with
        {
            Checks = codes,
            Cards = baseline.Cards.Select(c => c with { Applicable = Reverse(c.Applicable), Filled = Reverse(c.Filled) }).ToList(),
        };

        Assert.Empty(Build(edition, Vinnytsia, reversed).Week);
    }

    [Fact]
    public async Task Workbook_has_three_sheets_the_author_and_no_library_name()
    {
        var edition = await EditionAsync();
        var digest = Build(edition, Vinnytsia, SameAsNow(edition, DateTimeOffset.Parse("2026-11-18T01:12:54Z")));
        using var file = new MemoryStream();

        await DigestWorkbook.WriteAsync(file, digest, CancellationToken.None);
        var parts = Parts(file.ToArray());

        Assert.Contains("<dc:creator>Максим Гаркуша</dc:creator>", parts["docProps/core.xml"], StringComparison.Ordinal);
        Assert.Contains("docProps/core.xml", parts["[Content_Types].xml"], StringComparison.Ordinal);
        Assert.Contains("core-properties", parts["_rels/.rels"], StringComparison.Ordinal);
        Assert.False(parts.ContainsKey("docProps/app.xml"));
        Assert.All(parts.Values, text => Assert.DoesNotContain("miniexcel", text, StringComparison.OrdinalIgnoreCase));
        foreach (string sheet in new[] { DigestWorkbook.SummarySheet, DigestWorkbook.CardsSheet, DigestWorkbook.WeekSheet })
            Assert.Contains($"name=\"{sheet}\"", parts["xl/workbook.xml"], StringComparison.Ordinal);
        Assert.Contains("Історія ведеться з 18.11.2026", CellText(parts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_is_one_a4_page_with_the_numbers_and_neutral_properties()
    {
        var digest = Build(await EditionAsync(), Vinnytsia);

        byte[] pdf = DigestPdf.Write(digest);
        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        string text = string.Join(" ", page.GetWords().Select(w => w.Text));

        Assert.Equal(1, document.NumberOfPages);
        Assert.Equal((595, 842), ((int)page.Width, (int)page.Height));
        Assert.Contains("Вінницькій", text, StringComparison.Ordinal);
        // PdfPig читає слова рядками: спершу три числа, під ними підписи
        Assert.Contains("16 14 0 відкритих карток заповнено все доречне оновлено за тиждень", text, StringComparison.Ordinal);
        Assert.Contains("https://dream-povnota.onrender.com/org/25845655", text, StringComparison.Ordinal);
        Assert.Equal("Максим Гаркуша", document.Information.Author);
        Assert.Equal("dream-povnota", document.Information.Producer);
        Assert.Equal("dream-povnota", document.Information.Creator);
        Assert.DoesNotContain("pdfpig", Encoding.Latin1.GetString(pdf), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pdf_renders_every_organisation_title_on_one_page()
    {
        var edition = new Edition(DateTimeOffset.Parse("2026-11-18T05:11:22Z"), null, [], new EditionSlice("all", "Усі", 0, 0, 0, []), [], [], [], [], []);

        foreach (var organisation in DigestOrganisations.All)
        {
            using var document = PdfDocument.Open(DigestPdf.Write(DigestBuilder.Build(edition, organisation, null)));
            Assert.Equal(1, document.NumberOfPages);
        }
    }

    [Fact]
    public async Task Files_carry_no_personal_data_and_no_other_organisation()
    {
        var edition = await EditionAsync();
        // підприємець у тих самих даних: його картка не має потрапити до жодного зрізу
        var organisations = edition.Organisations.Append(new EditionOrganisation("n:Фізична особа-підприємець", null, "Фізична особа-підприємець", 1, 0.5)).ToList();
        var cards = edition.Cards.Append(edition.Cards[0] with { Code = "010125-BBBBBBBB", Title = "Ремонт магазину", Organisation = organisations.Count - 1 }).ToList();
        edition = edition with { Organisations = organisations, Cards = cards };

        foreach (string edrpou in new[] { Agency, Vinnytsia, Cherkasy })
        {
            var digest = Build(edition, edrpou);
            using var xlsx = new MemoryStream();
            await DigestWorkbook.WriteAsync(xlsx, digest, CancellationToken.None);
            string text = CellText(Parts(xlsx.ToArray()));
            using var pdf = PdfDocument.Open(DigestPdf.Write(digest));
            text += string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text));

            Assert.DoesNotContain("010125-BBBBBBBB", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Фізична особа", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ФОП", text, StringComparison.Ordinal);
            // РНОКПП має 10 цифр; ЄДРПОУ, крім самої організації, теж не має бути
            Assert.Empty(TenDigits().Matches(text));
            Assert.All(EightDigits().Matches(text).Select(m => m.Value).Distinct(),
                code => Assert.Equal(edrpou, code));
        }
    }

    private static Dictionary<string, string> Parts(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        });
    }

    /// <summary>Текст і числа клітинок усіх аркушів, без розмітки й атрибутів (ширин колонок тощо).</summary>
    private static string CellText(Dictionary<string, string> parts) => string.Join("\n",
        parts.Where(p => p.Key.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) || p.Key == "xl/sharedStrings.xml")
            .SelectMany(p => CellValue().Matches(p.Value).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))));

    [GeneratedRegex(@"<(?:x:)?[tv](?: [^>]*)?>([^<]*)</(?:x:)?[tv]>")]
    private static partial Regex CellValue();

    [GeneratedRegex(@"(?<![0-9])[0-9]{10}(?![0-9])")]
    private static partial Regex TenDigits();

    [GeneratedRegex(@"(?<![0-9A-F-])[0-9]{8}(?![0-9])")]
    private static partial Regex EightDigits();
}
