using DreamPovnota.Stats;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Pages.Shared;

/// <summary>Спільне для сторінок організації й області: картки, підсумки і фільтр для посилань.</summary>
public sealed record SliceView(
    LoadedEdition Edition,
    string Title,
    IReadOnlyList<int> Cards,
    IReadOnlyDictionary<string, string> Filter,
    string ApiPath)
{
    public int Complete => Cards.Count(i => Edition.IsComplete(Edition.Edition.Cards[i]));

    public double AverageScore => Cards.Count == 0 ? 0 : Cards.Average(i => Edition.Score(Edition.Edition.Cards[i]));

    public int SeekingFunding => Cards.Count(i => Edition.Edition.Cards[i].SeeksFunding);

    public IReadOnlyList<CheckTally> Tallies => Edition.Tally(Cards.ToList());

    public string Query => Microsoft.AspNetCore.Http.QueryString.Create(
        Filter.Select(kv => KeyValuePair.Create(kv.Key, (string?)kv.Value))).Value ?? "";
}
