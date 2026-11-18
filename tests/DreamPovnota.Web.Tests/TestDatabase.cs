using Dapper;
using DreamPovnota.Web.History;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DreamPovnota.Web.Tests;

/// <summary>Окрема порожня база на кожен тест; після тесту видаляється.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public static string? ServerUrl => Environment.GetEnvironmentVariable("TEST_DATABASE_URL");

    private readonly string name = "t_" + Guid.NewGuid().ToString("N");

    private TestDatabase()
    {
    }

    public string Url { get; private set; } = "";

    public Db Db { get; private set; } = null!;

    public HistoryReader Reader => new(Db, NullLogger<HistoryReader>.Instance);

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase();
        await using (var server = new NpgsqlConnection(Db.ToConnectionString(ServerUrl!).ConnectionString))
        {
            await server.OpenAsync();
            await server.ExecuteAsync($"create database {database.name}");
        }

        var uri = new UriBuilder(ServerUrl!) { Path = "/" + database.name };
        database.Url = uri.Uri.ToString();
        database.Db = Open(database.Url);
        return database;
    }

    /// <summary>Порожня адреса дає Db без бази, як без DATABASE_URL.</summary>
    public static Db Open(string url) => new(
        new ConfigurationBuilder().AddInMemoryCollection([new("DATABASE_URL", url)]).Build(),
        TimeProvider.System,
        NullLogger<Db>.Instance);

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Db.Source!.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var server = new NpgsqlConnection(Db.ToConnectionString(ServerUrl!).ConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"drop database if exists {name} with (force)");
    }
}
