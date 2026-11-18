using System.Net;
using DreamPovnota.Web.History;
using Npgsql;

namespace DreamPovnota.Web.Tests;

public class DbTests
{
    [Fact]
    public void Render_internal_address_without_port_or_ssl_is_understood()
    {
        var (connectionString, ignored) = Db.ToConnectionString("postgresql://dream_povnota:secret@dpg-abc123-a/dream_povnota");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("dpg-abc123-a", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("dream_povnota", builder.Database);
        Assert.Equal("dream_povnota", builder.Username);
        Assert.Equal("secret", builder.Password);
        Assert.Equal(5, builder.MaxPoolSize);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Neon_pooled_address_keeps_ssl_and_channel_binding()
    {
        var (connectionString, _) = Db.ToConnectionString(
            "postgres://user:pw@ep-cool-name-pooler.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.Equal(ChannelBinding.Require, builder.ChannelBinding);
        Assert.Equal("neondb", builder.Database);
    }

    [Fact]
    public void Percent_encoded_password_is_decoded()
    {
        var (connectionString, _) = Db.ToConnectionString("postgres://u:p%40ss%3Aw%2Frd@host:6543/db");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("p@ss:w/rd", builder.Password);
        Assert.Equal(6543, builder.Port);
    }

    [Fact]
    public void Unknown_parameters_are_reported_by_name()
    {
        var (_, ignored) = Db.ToConnectionString("postgres://u:p@host/db?application_name=x&sslmode=prefer");

        Assert.Equal(["application_name"], ignored);
    }

    [Theory]
    [InlineData("mysql://u:p@host/db")]
    [InlineData("host=localhost;database=db")]
    public void Address_that_is_not_postgres_is_refused(string url)
    {
        Assert.Throws<FormatException>(() => Db.ToConnectionString(url));
    }

    [Fact]
    public async Task Pages_still_render_with_a_notice_when_the_database_is_unreachable()
    {
        await using var factory = new WebFactory { DatabaseUrl = "postgres://nobody:nothing@127.0.0.1:1/none" };
        var client = await factory.ReadyClientAsync();
        var organisation = (await Fixtures.LoadedAsync()).OrganisationList[0];

        var response = await client.GetAsync("/org/" + Uri.EscapeDataString(organisation.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Історія змін тимчасово недоступна", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/snapshots")).StatusCode);
    }

    [Fact]
    public async Task Without_a_database_pages_say_history_is_unavailable()
    {
        await using var factory = new WebFactory();
        var client = await factory.ReadyClientAsync();
        string region = (await Fixtures.LoadedAsync()).Edition.Regions[0].Key;

        string html = await client.GetStringAsync("/region/" + region);

        Assert.Contains("Історія змін тимчасово недоступна", html);
    }
}
