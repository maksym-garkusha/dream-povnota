using DreamPovnota.Web.Api;
using DreamPovnota.Web.Import;
using DreamPovnota.Web.Lists;
using Microsoft.AspNetCore.OutputCaching;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Кеш відповідей до наступного випуску. У ключі покоління <see cref="EditionStore.Generation"/>,
/// тож відповідь, зібрана до нового випуску чи знімка, після них уже не віддається.
/// </summary>
public static class CachePolicies
{
    public static void Configure(OutputCacheOptions o)
    {
        o.SizeLimit = 64 * 1024 * 1024;
        o.MaximumBodySize = 8 * 1024 * 1024;
        o.AddPolicy(ImportService.CacheTag, policy => policy
            .Expire(ImportService.Interval)
            .SetVaryByQuery("*")
            .VaryByValue(Generation)
            .Tag(ImportService.CacheTag));
        // вивантаження кешуються тільки за параметрами фільтра; повний файл без фільтра лежить готовим
        // на диску (ExportFiles), тож у кеш у пам'яті його не кладемо
        o.AddPolicy(CardsApi.ExportCachePolicy, policy => policy
            .Expire(ImportService.Interval)
            .SetVaryByQuery(CardFilter.FilterNames)
            .VaryByValue(Generation)
            .With(context => CardFilter.FilterNames.Any(name => !string.IsNullOrWhiteSpace(context.HttpContext.Request.Query[name])))
            .Tag(ImportService.CacheTag));
    }

    private static KeyValuePair<string, string> Generation(HttpContext context) =>
        new("generation", context.RequestServices.GetRequiredService<EditionStore>().Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
