using System.Text.Json;
using DreamPovnota.Cards;

namespace DreamPovnota.Tests;

public class CardParserTests
{
    private static ProjectCard Project(string fixture) => CardParser.ParseProject(Fixture.Text(fixture));

    [Fact]
    public void Active_project_budget_and_funding_match_the_portal()
    {
        // DREAM-UA-190424-A10FC799 на порталі: орієнтовний бюджет 3 735 998 грн,
        // підтверджені джерела фінансування 3 735 998 грн
        var card = Project("project-active.json");

        Assert.Equal("DREAM-UA-190424-A10FC799", card.Code);
        Assert.Equal(3_735_998m, card.CostEstimate);
        Assert.Equal(3_735_998m, card.FundingCommitted);
        Assert.Equal(3_735_998m, card.FundingTotal);
        Assert.Equal(0m, card.FundingIndicative);
        Assert.Equal(["centralBudget", "localBudget"], card.FinanceCategories.Order());
    }

    [Fact]
    public void Active_project_stage_comes_from_internal_status()
    {
        var card = Project("project-active.json");

        Assert.Equal("active:approved", card.Status);
        Assert.Equal(Stage.Implementation, card.Stage);
        Assert.True(card.IsOpen);
    }

    [Fact]
    public void Cancelled_contracting_process_is_not_counted()
    {
        // у картці два процеси, другий скасовано
        var card = Project("project-active.json");

        Assert.Equal(1, card.ContractingProcesses);
        Assert.True(card.HasProzorroLink);
        Assert.Equal(1, card.ContractCount);
        // у договору з Prozorro суми в DREAM немає
        Assert.Equal(0m, card.ContractsValue);
    }

    [Fact]
    public void Organisation_is_the_project_author_not_the_initiator()
    {
        var card = Project("project-active.json");

        Assert.Equal("04412567", card.Organisation?.Edrpou);
        Assert.Equal("ПОНОРНИЦЬКА СЕЛИЩНА РАДА", card.Organisation?.Name);
        Assert.Contains(card.Parties, p => p.HasRole("initiator") && p.Edrpou == "44045606");
    }

    [Fact]
    public void Location_objects_and_documents_come_from_approaches_and_related_processes()
    {
        var card = Project("project-active.json");

        Assert.Equal(["UA74060050020095773"], card.Katottg);
        Assert.Equal(["urp872q7-4zyv-4n1o-afpe-cu4b4r244ciq"], card.ObjectIds);
        // 2 документи проєкту і 2 в approaches
        Assert.Equal(4, card.DocumentCount);
    }

    [Fact]
    public void Active_project_has_english_title_and_description()
    {
        var card = Project("project-active.json");

        Assert.True(card.HasEnglishTitle);
        Assert.True(card.HasEnglishDescription);
        Assert.Equal(TextState.Filled, card.DescriptionState);
        Assert.Equal(TextState.Filled, card.GoalState);
        Assert.Equal(6m, card.DurationMonths);
    }

    [Fact]
    public void Sector_letter_is_the_first_letter_of_the_world_bank_code()
    {
        var card = Project("project-active.json");

        Assert.Equal("ES", card.SectorCode);
        Assert.Equal('E', card.SectorLetter);
        Assert.Equal("Освіта", Sectors.Name(card.SectorLetter));
    }

    [Fact]
    public void Null_description_is_a_stub()
    {
        var card = Project("project-appraised.json");

        Assert.Equal(TextState.Stub, card.DescriptionState);
        Assert.Equal(4, card.DescriptionLength);
    }

    [Fact]
    public void Appraised_project_without_purpose_period_and_sector()
    {
        var card = Project("project-appraised.json");

        Assert.Equal(Stage.Appraised, card.Stage);
        Assert.Equal(TextState.Empty, card.GoalState);
        Assert.Null(card.DurationMonths);
        Assert.Null(card.SectorCode);
        Assert.Null(card.SectorLetter);
        Assert.Equal(Sectors.NotSpecified, Sectors.Name(card.SectorLetter));
    }

    [Fact]
    public void Indicative_funding_is_not_committed()
    {
        var card = Project("project-appraised.json");

        Assert.Equal(12_235_130m, card.CostEstimate);
        Assert.Equal(10_200_000m, card.FundingTotal);
        Assert.Equal(10_200_000m, card.FundingIndicative);
        Assert.Equal(0m, card.FundingCommitted);
    }

    [Fact]
    public void Source_with_zero_amount_is_counted_but_adds_nothing()
    {
        // чотири індикативні джерела, одне з них на 0 грн
        var card = Project("project-english.json");

        Assert.Equal(4, card.FundingSourceCount);
        Assert.Equal(61_050_000m, card.FundingTotal);
        Assert.Equal(309_319_999.98m, card.CostEstimate);
    }

    [Fact]
    public void Top_level_deficient_budget_is_ignored()
    {
        // у cdu_response.budget лише рядки "deficient" без сум, кошторис в approaches
        var card = Project("project-active-no-contracting.json");

        Assert.Equal(849_892m, card.CostEstimate);
        Assert.Equal(46_280m, card.FundingCommitted);
        Assert.Equal(0, card.ContractingProcesses);
        Assert.False(card.HasProzorroLink);
        Assert.False(card.HasEnglishTitle);
    }

    [Fact]
    public void Completed_project_keeps_kopecks_and_completion()
    {
        var card = Project("project-complete.json");

        Assert.Equal(Stage.Completed, card.Stage);
        Assert.True(card.HasCompletion);
        Assert.Equal(140_300_325.00m, card.CostEstimate);
        Assert.Equal(140_300_325m, card.FundingIndicative);
        // один код двічі в різних items
        Assert.Equal(["UA65100170060084047"], card.Katottg);
    }

    [Fact]
    public void Cancelled_project_is_closed()
    {
        var card = Project("project-cancelled.json");

        Assert.Equal("cancelled:mistaken", card.Status);
        Assert.Equal(Stage.Closed, card.Stage);
        Assert.False(card.IsOpen);
    }

    [Fact]
    public void Unsuccessful_project_is_closed_and_has_no_documents()
    {
        var card = Project("project-unsuccessful.json");

        Assert.Equal(Stage.Closed, card.Stage);
        Assert.Equal(0, card.DocumentCount);
        Assert.Equal(["UA46000000000026241"], card.Katottg);
    }

    [Fact]
    public void Idea_has_no_approaches_and_no_money()
    {
        var card = Project("project-idea.json");

        Assert.Equal(Stage.Idea, card.Stage);
        Assert.Empty(card.Katottg);
        Assert.Equal(0m, card.CostEstimate);
        Assert.Equal(0, card.FundingSourceCount);
        Assert.Equal(1, card.DocumentCount);
        Assert.Equal("W", card.SectorCode?[..1]);
    }

    [Fact]
    public void Defined_project_has_long_html_description_and_duration()
    {
        var card = Project("project-defined.json");

        Assert.Equal(Stage.Defined, card.Stage);
        Assert.Equal(TextState.Filled, card.DescriptionState);
        Assert.DoesNotContain(card.Title, "  ");
        Assert.Equal(9m, card.DurationMonths);
    }

    [Fact]
    public void Sole_proprietor_keeps_role_but_not_tax_number_or_name()
    {
        var card = Project("project-individual.json");

        var person = Assert.Single(card.Parties, p => p.IsSoleProprietor);
        Assert.Null(person.Edrpou);
        Assert.Null(person.Name);
        Assert.Equal(["technicalDesigner"], person.Roles);
        Assert.Equal(3, card.Parties.Count);
    }

    [Theory]
    [InlineData("project-individual.json")]
    [InlineData("project-active.json")]
    [InlineData("project-cancelled.json")]
    public void Parsed_card_has_no_personal_data(string fixture)
    {
        string json = JsonSerializer.Serialize(Project(fixture));

        Assert.DoesNotContain("1234567899", json);
        Assert.DoesNotContain("Вигадан", json);
    }

    [Theory]
    [InlineData("UA-IPN", "1234567899")]
    [InlineData("UA-EDR", "1234567899")]   // РНОКПП під схемою UA-EDR
    [InlineData("ua-ipn", "1234567899")]
    [InlineData(" Ua-Ipn ", "1234567899")]
    [InlineData("UA-EDR", "123 456 7899")]
    [InlineData("UA-EDR", "123-456-7899")]
    [InlineData("ua-edr", " 1234567899 ")]
    public void Sole_proprietor_is_recognised_by_scheme_or_ten_digits(string scheme, string code)
    {
        var party = ParseParty(scheme, code, "ФОП Вигаданий Іван Іванович");

        Assert.Equal(PartyKind.SoleProprietor, party.Kind);
        Assert.Null(party.Edrpou);
        Assert.Null(party.Name);
    }

    [Theory]
    [InlineData("UA-EDR", "123456789", "ФОП Вигаданий Іван Іванович")]
    [InlineData("XX-UNKNOWN", "1", "фоп Вигаданий І. І.")]
    [InlineData("UA-EDR", "", "Фізична особа-підприємець Вигаданий Іван")]
    public void Party_named_as_a_sole_proprietor_keeps_neither_name_nor_code(string scheme, string code, string name)
    {
        var party = ParseParty(scheme, code, name);

        Assert.Equal(PartyKind.SoleProprietor, party.Kind);
        Assert.Null(party.Name);
        Assert.Null(party.Edrpou);
    }

    [Fact]
    public void Edr_code_with_spaces_is_still_an_organisation_code()
    {
        var party = ParseParty(" ua-edr", "0441 2567", "РАДА");

        Assert.Equal(PartyKind.Organisation, party.Kind);
        Assert.Equal("04412567", party.Edrpou);
        Assert.Equal("UA-EDR", party.Scheme);
    }

    [Fact]
    public void Edr_code_of_wrong_length_keeps_name_but_not_code()
    {
        var party = ParseParty("UA-EDR", "123456789", "ТОВ \"БУДІВЕЛЬНИК\"");

        Assert.Equal(PartyKind.OddCode, party.Kind);
        Assert.Null(party.Edrpou);
        Assert.Equal("ТОВ \"БУДІВЕЛЬНИК\"", party.Name);
    }

    [Fact]
    public void Foreign_party_keeps_name_and_scheme()
    {
        var party = ParseParty("FR-COA", "123 456 789", "Agence Française de Développement");

        Assert.Equal(PartyKind.OtherRegistry, party.Kind);
        Assert.Equal("FR-COA", party.Scheme);
        Assert.Equal("Agence Française de Développement", party.Name);
        Assert.Null(party.Edrpou);
    }

    [Fact]
    public void Edr_code_given_as_number_is_read()
    {
        string json = Minimal("""
            "parties": [{"identifier": {"id": 37573094, "scheme": "UA-EDR", "legalName": "РАДА"}, "roles": ["projectAuthor"]}]
            """);

        var party = Assert.Single(CardParser.ParseProject(json).Parties);
        Assert.Equal("37573094", party.Edrpou);
    }

    [Fact]
    public void Unexpected_types_and_missing_sections_do_not_break_parsing()
    {
        string json = Minimal("""
            "title": "не масив",
            "period": {"durationInMonth": "дванадцять"},
            "parties": [{"identifier": null, "roles": "projectAuthor"}],
            "approaches": [{"budget": {"budgetBreakdown": [{"amount": {"amount": "1500.50"}}], "finance": [{"value": null}]}}],
            "contractingProcesses": [{"summary": null}]
            """);

        var card = CardParser.ParseProject(json);

        Assert.Equal("", card.Title);
        Assert.Null(card.DurationMonths);
        Assert.Equal(1500.50m, card.CostEstimate);
        Assert.Equal(1, card.FundingSourceCount);
        Assert.Equal(1, card.ContractingProcesses);
        Assert.Equal(PartyKind.OtherRegistry, Assert.Single(card.Parties).Kind);
    }

    [Fact]
    public void Code_falls_back_to_internal_code()
    {
        var card = CardParser.ParseProject(Minimal(""));

        Assert.Equal("DREAM-UA-010101-AAAAAAAA", card.Code);
    }

    [Theory]
    [InlineData("{\"internal\": {\"id\": \"x\"}}")]   // немає cdu_response
    [InlineData("{\"internal\": {}, \"cdu_response\": {\"updated\": \"2025-01-01T00:00:00Z\"}}")]
    [InlineData("{\"internal\": {\"id\": \"x\", \"code\": \"c\"}, \"cdu_response\": {}}")]   // немає updated
    [InlineData("<html>502 Bad Gateway</html>")]
    public void Card_that_cannot_be_read_throws_json_exception(string json)
    {
        Assert.ThrowsAny<JsonException>(() => CardParser.ParseProject(json));
    }

    [Fact]
    public void Object_card_has_location_and_documents()
    {
        var card = CardParser.ParseObject(Fixture.Text("object-active.json"));

        Assert.Equal("urp872q7-4zyv-4n1o-afpe-cu4b4r244ciq", card.Id);
        Assert.Equal(["UA74060050020095773"], card.Katottg);
        Assert.Equal(2, card.DocumentCount);
        Assert.Equal(DateTimeOffset.Parse("2026-04-15T09:48:33.243202Z"), card.Updated);
    }

    [Fact]
    public void Object_card_has_no_personal_data()
    {
        string json = JsonSerializer.Serialize(CardParser.ParseObject(Fixture.Text("object-defined.json")));

        Assert.DoesNotContain("Вигадан", json);
    }

    private static Party ParseParty(string scheme, string code, string name)
    {
        string json = Minimal($$"""
            "parties": [{"identifier": {"id": "{{code}}", "scheme": "{{scheme}}", "legalName": "{{name.Replace("\"", "\\\"")}}"}, "roles": ["technicalDesigner"]}]
            """);
        return Assert.Single(CardParser.ParseProject(json).Parties);
    }

    private static string Minimal(string fields) => $$"""
        {
          "internal": {"id": "test-id", "code": "010101-AAAAAAAA", "status": "pending:initiated"},
          "cdu_response": {
            "updated": "2025-01-01T00:00:00Z"{{(fields.Length > 0 ? "," : "")}}
            {{fields}}
          }
        }
        """;
}
