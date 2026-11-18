using DreamPovnota.Export;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Api;

internal static class ApiMapping
{
    public static CardItem Card(LoadedEdition edition, EditionCard card)
    {
        var e = edition.Edition;
        var organisation = edition.OrganisationOf(card);
        var region = e.Regions[card.Region];
        return new CardItem(
            card.Code,
            LoadedEdition.PortalPrefix + card.Code + "/profile",
            card.Title,
            new OrganisationRef(organisation.Key, organisation.Edrpou, organisation.Title),
            new RegionRef(region.Key, region.Title),
            e.Stages[card.Stage].Key,
            e.Sectors[card.Sector].Key,
            card.Updated,
            edition.ScoreFilled(card),
            edition.ScoreApplicable(card),
            edition.Missing(card).Select(c => c.Code).ToList(),
            card.SeeksFunding);
    }

    public static SliceItem Slice(EditionSlice slice, IReadOnlyList<EditionCheck> checks) => new(
        slice.Key,
        slice.Title,
        slice.Open,
        slice.Complete,
        slice.AverageScore,
        checks.Select((c, i) => new CheckCount(c.Code, slice.Checks[i].Applicable, slice.Checks[i].Filled)).ToList());

    public static SliceItem Slice(LoadedEdition edition, OrganisationEntry organisation)
    {
        var tallies = edition.Tally(edition.CardsByOrganisation[organisation.Key]);
        return new SliceItem(
            organisation.Key,
            organisation.Title,
            organisation.Open,
            organisation.Complete,
            Math.Round(organisation.AverageScore, 6),
            edition.Edition.Checks.Select((c, i) => new CheckCount(c.Code, tallies[i].Applicable, tallies[i].Filled)).ToList());
    }

    public static IResult NotFound(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Не знайдено", detail: detail);
}
