using DreamPovnota.Web.Live;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DreamPovnota.Web.Tests;

/// <summary>
/// Застосунок цілком: data.json з фікстур, DREAM підставний. Кожен тестовий клас
/// отримує свій екземпляр, тож ліміти частоти одного класу не заважають іншому.
/// </summary>
public sealed class WebFactory : WebApplicationFactory<Program>
{
    public FakeDream Dream { get; } = new();

    public string DataUrl { get; set; } = Fixtures.EditionPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DATA_URL", DataUrl);
        builder.ConfigureServices(services =>
            services.AddHttpClient<LiveCheck>().ConfigurePrimaryHttpMessageHandler(() => Dream));
    }

    /// <summary>Клієнт, коли випуск уже завантажено.</summary>
    public async Task<HttpClient> ReadyClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (int i = 0; i < 100; i++)
        {
            using var response = await client.GetAsync("/health/ready");
            if (response.IsSuccessStatusCode) return client;
            await Task.Delay(50);
        }
        throw new TimeoutException("Випуск не завантажився");
    }
}
