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

    /// <summary>Порожньо: історія вимкнена, як локально без бази.</summary>
    public string DatabaseUrl { get; set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DATA_URL", DataUrl);
        builder.UseSetting("DATABASE_URL", DatabaseUrl);
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
