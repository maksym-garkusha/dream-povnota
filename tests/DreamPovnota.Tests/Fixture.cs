namespace DreamPovnota.Tests;

/// <summary>
/// Файли з папки Fixtures. Це справжні відповіді API DREAM: імена людей і дані
/// підприємця в них замінено вигаданими, довгі масиви обрізано.
/// </summary>
public static class Fixture
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(Path(name));
}
