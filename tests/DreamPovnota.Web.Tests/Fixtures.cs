using DreamPovnota.Cards;
using DreamPovnota.Export;
using DreamPovnota.Stats;
using DreamPovnota.Web.Import;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Справжні відповіді DREAM з тестів розбору і data.json, зібраний з них тим самим
/// кодом, що в CLI. Файл пишеться один раз на запуск тестів.
/// </summary>
public static class Fixtures
{
    public static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-11-17T22:00:00Z");

    public static readonly string[] Projects =
    [
        "project-active.json", "project-active-no-contracting.json", "project-appraised.json",
        "project-cancelled.json", "project-complete.json", "project-defined.json", "project-english.json",
        "project-idea.json", "project-individual.json", "project-unsuccessful.json",
    ];

    public static readonly string[] Objects = ["object-active.json", "object-defined.json", "object-idea.json"];

    private static readonly Lazy<string> EditionFile = new(WriteEdition);

    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static Summary Summary() => Stats.Summary.Build(
        Projects.Select(f => CardParser.ParseProject(Text(f))).ToList(),
        Objects.Select(f => CardParser.ParseObject(Text(f))),
        AsOf);

    /// <summary>Шлях до data.json з фікстур.</summary>
    public static string EditionPath => EditionFile.Value;

    public static async Task<LoadedEdition> LoadedAsync()
    {
        await using var file = File.OpenRead(EditionPath);
        return new LoadedEdition(await Edition.ReadAsync(file), "test");
    }

    private static string WriteEdition()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dream-povnota-web-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, DataExport.FileName);
        using (var file = File.Create(path)) DataExport.Write(Summary(), file);
        return path;
    }
}
