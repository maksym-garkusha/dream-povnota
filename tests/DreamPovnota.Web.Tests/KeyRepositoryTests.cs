using System.Net;
using System.Xml.Linq;
using DreamPovnota.Web.History;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamPovnota.Web.Tests;

public class KeyRepositoryTests
{
    [PostgresFact]
    public async Task Stored_key_is_read_back_by_the_next_process()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Migrator.MigrateAsync(database.Db, CancellationToken.None);

        new DbKeyRepository(database.Db, NullLogger<DbKeyRepository>.Instance)
            .StoreElement(new XElement("key", new XAttribute("id", "k1")), "key-k1");
        var read = new DbKeyRepository(database.Db, NullLogger<DbKeyRepository>.Instance).GetAllElements();

        Assert.Equal("k1", Assert.Single(read).Attribute("id")!.Value);
    }

    [Fact]
    public void Without_a_database_keys_live_in_memory()
    {
        var repository = new DbKeyRepository(TestDatabase.Open(""), NullLogger<DbKeyRepository>.Instance);

        repository.StoreElement(new XElement("key", new XAttribute("id", "k1")), "key-k1");

        Assert.Single(repository.GetAllElements());
    }

    [PostgresFact]
    public async Task Check_form_opened_before_a_restart_still_works_after_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        string token, cookie;
        await using (var before = new WebFactory { DatabaseUrl = database.Url })
        {
            var client = await before.ReadyClientAsync();
            await WaitForSnapshot(database);
            var page = await client.GetAsync("/check");
            cookie = page.Headers.GetValues("Set-Cookie").First().Split(';')[0];
            token = System.Text.RegularExpressions.Regex.Match(await page.Content.ReadAsStringAsync(),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        }
        Assert.Equal(1L, await database.ScalarAsync<long>("select count(*) from data_protection_key"));

        await using var after = new WebFactory { DatabaseUrl = database.Url };
        var restarted = after.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        await after.ReadyClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/check")
        {
            Content = new FormUrlEncodedContent([new("code", "nonsense"), new("__RequestVerificationToken", token)]),
        };
        request.Headers.Add("Cookie", cookie);
        var response = await restarted.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Сторінка застаріла", html);
        Assert.Contains("Не схоже на код DREAM", html);
    }

    private static async Task WaitForSnapshot(TestDatabase database)
    {
        // прямо в базі, а не через /api/v1/snapshots: та відповідь кешується, і порожній перелік,
        // прочитаний за мить до запису знімка, міг лягти в кеш уже після його очищення
        for (int i = 0; i < 300; i++)
        {
            if (await database.ScalarAsync<bool>("select to_regclass('snapshot') is not null")
                && await database.ScalarAsync<long>("select count(*) from snapshot") > 0)
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Знімок не записався");
    }
}
