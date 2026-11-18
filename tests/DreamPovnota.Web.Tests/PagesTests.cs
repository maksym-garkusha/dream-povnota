using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DreamPovnota.Cards;

namespace DreamPovnota.Web.Tests;

public partial class PagesTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;

    public PagesTests(WebFactory factory) => this.factory = factory;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [Theory]
    [InlineData("/", "Наповненість карток проєктів")]
    [InlineData("/cards", "Відкриті картки")]
    [InlineData("/orgs", "Організації")]
    [InlineData("/about", "Як рахується")]
    [InlineData("/check", "Перевірити картку зараз")]
    public async Task Page_answers_with_its_ukrainian_heading(string path, string heading)
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync(path);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(heading, html);
        Assert.Contains("Не пов'язаний з державними органами", html);
    }

    [Fact]
    public async Task Home_says_it_is_not_official_statistics_before_the_check_form()
    {
        var client = await factory.ReadyClientAsync();

        string html = await client.GetStringAsync("/");

        int disclaimer = html.IndexOf("Незалежний навчальний проєкт на відкритих даних, не офіційна статистика.", StringComparison.Ordinal);
        Assert.InRange(disclaimer, 0, html.IndexOf("<form", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Navigation_marks_the_current_section_and_leads_to_changes_and_digest()
    {
        var client = await factory.ReadyClientAsync();

        string digest = await client.GetStringAsync("/digest/25845655");
        string home = await client.GetStringAsync("/");

        Assert.Contains("<a href=\"/digest\" aria-current=\"page\">Зріз для служб</a>", digest);
        Assert.Contains("<a href=\"/changes\">Зміни за тижнями</a>", home);
        Assert.DoesNotContain("aria-current", home);
    }

    [Fact]
    public async Task Check_page_has_one_heading_for_the_form_and_the_card_title_as_heading_of_a_result()
    {
        var client = await factory.ReadyClientAsync();
        var edition = await Fixtures.LoadedAsync();
        var card = edition.Edition.Cards[0];

        string empty = await client.GetStringAsync("/check");
        string snapshot = await client.GetStringAsync("/check/" + card.Code);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(empty, "<h[12][ >]"));
        Assert.Contains($"<h1 class=\"page\" id=\"page-title\">{System.Net.WebUtility.HtmlEncode(card.Title)}</h1>", snapshot);
        Assert.Contains(">Перевірити цю картку в DREAM</button>", snapshot);
        Assert.Contains(">Перевірити</button>", snapshot);
    }

    [Fact]
    public async Task About_names_the_stages_where_each_check_applies_in_words()
    {
        var client = await factory.ReadyClientAsync();

        string html = await client.GetStringAsync("/about");

        Assert.Contains("<td data-label=\"Коли доречне\">усі стадії</td>", html);
        Assert.Contains("тільки «Реалізація»", html);
        Assert.Contains("з «Визначено»", html);
        Assert.DoesNotContain("…", html);
    }

    [Fact]
    public async Task Pages_carry_a_link_preview_with_an_absolute_image()
    {
        var client = await factory.ReadyClientAsync();

        string html = await client.GetStringAsync("/cards");
        var image = await client.GetAsync("/og.png");

        Assert.Contains("<meta property=\"og:image\" content=\"http://localhost/og.png\">", html);
        Assert.Contains("<meta property=\"og:title\" content=\"Картки · dream-povnota\">", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Organisation_and_region_pages_open_from_the_edition()
    {
        var client = await factory.ReadyClientAsync();
        var edition = await Fixtures.LoadedAsync();
        var organisation = edition.OrganisationList[0];
        string region = edition.Edition.Regions[0].Key;

        var orgPage = await client.GetAsync("/org/" + Uri.EscapeDataString(organisation.Key));
        var regionPage = await client.GetAsync("/region/" + region);

        Assert.Equal(HttpStatusCode.OK, orgPage.StatusCode);
        Assert.Contains("Що заповнити", await orgPage.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, regionPage.StatusCode);
    }

    [Fact]
    public async Task Sole_proprietors_have_no_organisation_page()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/org/" + Uri.EscapeDataString("n:Фізична особа-підприємець"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Сторінки немає", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_strict_policy()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/cards");

        string csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("max-age=300", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Wrong_filter_value_is_shown_next_to_its_field()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.GetAsync("/cards?region=99");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("id=\"region-error\"", html);
        Assert.Contains("Такої області в даних немає", html);
    }

    [Fact]
    public async Task Check_form_without_a_token_comes_back_with_a_friendly_message()
    {
        var client = await factory.ReadyClientAsync();

        var response = await client.PostAsync("/check", new FormUrlEncodedContent([new("code", "050923-B400F7B2")]));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Сторінка застаріла, натисніть ще раз", html);
        Assert.Matches(TokenPattern(), html);
        Assert.Equal(0, factory.Dream.Requests);
    }

    [Fact]
    public async Task Get_of_a_check_page_never_calls_dream()
    {
        var client = await factory.ReadyClientAsync();
        int before = factory.Dream.Requests;

        var response = await client.GetAsync("/check/050923-B400F7B2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, factory.Dream.Requests);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Health_endpoints_report_the_loaded_edition()
    {
        var client = await factory.ReadyClientAsync();

        Assert.Equal("ok", await client.GetStringAsync("/health/live"));
        using var ready = JsonDocument.Parse(await client.GetStringAsync("/health/ready"));
        Assert.True(ready.RootElement.GetProperty("edition").GetProperty("loaded").GetBoolean());
    }

    internal static async Task<string> TokenAsync(HttpClient client)
    {
        string html = await client.GetStringAsync("/check");
        return TokenPattern().Match(html).Groups[1].Value;
    }

    internal static string ActiveCode =>
        CardParser.ParseProject(Fixtures.Text("project-active.json")).Code["DREAM-UA-".Length..];
}
