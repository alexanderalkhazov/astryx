using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Astryx.Modules.Ai.Application.Context;

/// <summary>
/// Cleans provider news before it reaches a prompt (SYSTEM_DESIGN.md §12.4): decodes entities, strips HTML and URLs,
/// drops control characters and angle brackets (so text cannot close the <c>&lt;news&gt;</c> block), collapses
/// whitespace and truncates on a word boundary.
/// </summary>
public static partial class NewsSanitizer
{
    public const int HeadlineMax = 200;
    public const int SnippetMax = 400;

    public static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        value = Urls().Replace(value, " ");
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is '<' or '>' || char.IsControl(c) ? ' ' : c);
        }

        value = Spaces().Replace(builder.ToString(), " ").Trim();
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = value[..(maxLength - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > maxLength / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }

    [GeneratedRegex("</?[A-Za-z][^<>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Urls();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
