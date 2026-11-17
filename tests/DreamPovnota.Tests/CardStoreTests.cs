using System.Text.Json;
using DreamPovnota.Cards;
using DreamPovnota.Store;

namespace DreamPovnota.Tests;

public class CardStoreTests
{
    [Fact]
    public async Task Project_card_survives_the_round_trip()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        var card = CardParser.ParseProject(Fixture.Text("project-active.json"));

        await store.WriteProjectsAsync([card]);
        var read = Assert.Single((await store.ReadProjectsAsync()).Cards);

        // у записі є переліки, тому порівнюємо через JSON, а не через Equals
        Assert.Equal(JsonSerializer.Serialize(card), JsonSerializer.Serialize(read));
        Assert.Equal(Stage.Implementation, read.Stage);
        Assert.Equal("04412567", read.Organisation?.Edrpou);
    }

    [Fact]
    public async Task Object_card_survives_the_round_trip()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        var card = CardParser.ParseObject(Fixture.Text("object-idea.json"));

        await store.WriteObjectsAsync([card]);
        var read = Assert.Single((await store.ReadObjectsAsync()).Cards);

        Assert.Equal(card.Id, read.Id);
        Assert.Equal(card.Updated, read.Updated);
        Assert.Equal(card.Katottg, read.Katottg);
        Assert.Equal(card.DocumentCount, read.DocumentCount);
    }

    [Fact]
    public async Task Cache_file_is_one_readable_card_per_line()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        var cards = new[] { "project-idea.json", "project-complete.json" }
            .Select(f => CardParser.ParseProject(Fixture.Text(f)));

        await store.WriteProjectsAsync(cards);
        var lines = await File.ReadAllLinesAsync(dir.File(CardStore.ProjectsFile));

        Assert.Equal(2, lines.Length);
        Assert.Contains("Ямпіль", lines[0]);
        Assert.Contains("\"stage\":\"completed\"", lines[1]);
    }

    [Fact]
    public async Task Cache_has_no_personal_data()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        var cards = new[] { "project-individual.json", "project-active.json", "project-cancelled.json" }
            .Select(f => CardParser.ParseProject(Fixture.Text(f)));

        await store.WriteProjectsAsync(cards);
        await store.WriteObjectsAsync([CardParser.ParseObject(Fixture.Text("object-defined.json"))]);
        string text = await File.ReadAllTextAsync(dir.File(CardStore.ProjectsFile))
            + await File.ReadAllTextAsync(dir.File(CardStore.ObjectsFile));

        Assert.DoesNotContain("1234567899", text);
        Assert.DoesNotContain("Вигаданий", text);
        Assert.DoesNotContain("Вигадане", text);
    }

    [Fact]
    public async Task Missing_cache_reads_as_empty()
    {
        using var dir = new TempDir();

        var read = await new CardStore(dir.Path).ReadProjectsAsync();

        Assert.Empty(read.Cards);
        Assert.Equal(0, read.BadLines);
    }

    [Fact]
    public async Task Broken_line_is_skipped_and_counted()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        await store.WriteObjectsAsync([CardParser.ParseObject(Fixture.Text("object-active.json"))]);
        // так виглядає файл, якщо запис обірвався посеред рядка
        await File.AppendAllTextAsync(dir.File(CardStore.ObjectsFile), "{\"id\":\"x\",\"upda");

        var read = await store.ReadObjectsAsync();

        Assert.Single(read.Cards);
        Assert.Equal(1, read.BadLines);
    }

    [Fact]
    public async Task Writing_replaces_the_file_and_leaves_no_temporary_file()
    {
        using var dir = new TempDir();
        var store = new CardStore(dir.Path);
        var card = CardParser.ParseObject(Fixture.Text("object-active.json"));

        await store.WriteObjectsAsync([card, card with { Id = "other" }]);
        await store.WriteObjectsAsync([card]);

        Assert.Single((await store.ReadObjectsAsync()).Cards);
        Assert.Equal([CardStore.ObjectsFile], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }
}
