using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using DreamPovnota.Web.Api;
using DreamPovnota.Web.Feeds;
using DreamPovnota.Web.History;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using DreamPovnota.Web.Live;
using DreamPovnota.Web.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.WebEncoders;

var ukrainian = CultureInfo.GetCultureInfo("uk-UA");
CultureInfo.DefaultThreadCurrentCulture = ukrainian;
CultureInfo.DefaultThreadCurrentUICulture = ukrainian;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = 16 * 1024;
    kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    kestrel.Limits.MaxConcurrentConnections = 200;
});
builder.Services.Configure<FormOptions>(o =>
{
    o.ValueLengthLimit = 1024;
    o.ValueCountLimit = 10;
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // адреси проксі Render невідомі наперед, тож довіряємо рівно стільки переходів, скільки в Proxy:Hops
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    o.ForwardLimit = builder.Configuration.GetValue("Proxy:Hops", 1);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EditionStore>();
builder.Services.AddMemoryCache(o => o.SizeLimit = 500);

builder.Services.AddHttpClient(EditionSource.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = EditionSource.MaxBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DreamPovnota.Dream.DreamClient.UserAgent);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    });
// EditionSource пам'ятає ETag між запусками імпорту, тому він один на застосунок
builder.Services.AddSingleton(sp => new EditionSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(EditionSource.HttpClientName),
    sp.GetRequiredService<IConfiguration>()));

builder.Services.AddHttpClient<LiveCheck>(LiveCheck.Configure)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    });

builder.Services.AddSingleton<Db>();
// ключі токенів форм переживають сон екземпляра в базі; без бази (локально, тести) живуть у пам'яті
builder.Services.AddDataProtection().SetApplicationName("dream-povnota");
builder.Services.AddSingleton<DbKeyRepository>();
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<DbKeyRepository>((options, repository) => options.XmlRepository = repository);
builder.Services.AddSingleton<HistoryReader>();
builder.Services.AddSingleton<ExportFiles>();
builder.Services.AddSingleton(_ => new SharedLimits());
builder.Services.AddHostedService<ImportService>();

// кирилицю в HTML і JSON пишемо як є; небезпечні для розмітки знаки кодувальники екранують і далі
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "dp-af";
    // на Render запит приходить по https (схему дає X-Forwarded-Proto), і cookie отримує Secure;
    // Always тут не годиться: на http, як локально, антифорджері тоді кидає виняток замість сторінки
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
// рік без includeSubDomains і preload: ім'я на onrender.com належить хостингу, а не проєкту
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = false;
    o.Preload = false;
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "dream-povnota API";
    document.Info.Description =
        "Наповненість відкритих карток проєктів у DREAM за поточним випуском data.json. " +
        "Незалежний навчальний проєкт на відкритих даних, не пов'язаний з державними органами.";
    return Task.CompletedTask;
}));
builder.Services.AddOutputCache(o =>
{
    o.SizeLimit = 64 * 1024 * 1024;
    o.MaximumBodySize = 8 * 1024 * 1024;
    o.AddPolicy(ImportService.CacheTag, policy => policy
        .Expire(ImportService.Interval)
        .SetVaryByQuery("*")
        .Tag(ImportService.CacheTag));
    // вивантаження кешуються тільки за параметрами фільтра; повний файл без фільтра лежить готовим
    // на диску (ExportFiles), тож у кеш у пам'яті його не кладемо
    o.AddPolicy(CardsApi.ExportCachePolicy, policy => policy
        .Expire(ImportService.Interval)
        .SetVaryByQuery(CardFilter.FilterNames)
        .With(context => CardFilter.FilterNames.Any(name => !string.IsNullOrWhiteSpace(context.HttpContext.Request.Query[name])))
        .Tag(ImportService.CacheTag));
});
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ["text/html", "text/css", "application/json", "application/problem+json", "text/csv", "application/atom+xml", "image/svg+xml"];
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddRateLimiter(Limits.Configure);

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
// сторінки перевірки не стискаємо: у них токен форми поруч із введеним кодом (атака BREACH)
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/check"), branch => branch.UseResponseCompression());
app.UseExceptionHandler("/error");
app.UseStatusCodePagesWithReExecute("/status/{0}");
// API відповідає Problem Details і на невідому адресу, і на збій, а не HTML-сторінкою
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api =>
{
    api.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = ApiProblems.WriteErrorAsync });
    api.UseStatusCodePages(ApiProblems.WriteStatusAsync);
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseEditionGate();
app.UseOutputCache();

app.MapRazorPages();
app.MapOpenApi();
app.MapHealth();
app.MapCardsApi();
app.MapSlicesApi();
app.MapMetaApi();
app.MapExports();
app.MapHistoryApi();
app.MapFeeds();

app.Run();
