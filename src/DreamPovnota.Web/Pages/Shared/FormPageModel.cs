using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DreamPovnota.Web.Pages;

/// <summary>Сторінка з формою фільтра: помилки за назвою поля для показу поруч із ним.</summary>
public abstract class FormPageModel : PageModel
{
    public Dictionary<string, string[]> Errors { get; protected set; } = new(StringComparer.Ordinal);

    public FieldErrorModel Error(string field) =>
        new(field, Errors.TryGetValue(field, out var messages) ? messages[0] : null);

    public string Invalid(string field) => Errors.ContainsKey(field) ? "true" : "false";

    public string? Described(string field) => Errors.ContainsKey(field) ? field + "-error" : null;
}

public sealed record FieldErrorModel(string Field, string? Message);

public sealed record PagerModel(int Page, int Pages, Func<int, string> Link);
