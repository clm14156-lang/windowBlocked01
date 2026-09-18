using System.Net;
using System.Text.RegularExpressions;

namespace FocusApp.Desktop.Services;

public static partial class WebsiteMetadataParser
{
    public static string? ParseDisplayName(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var value = FindMetaContent(html, "property", "og:site_name")
            ?? FindTitle(html)
            ?? FindMetaContent(html, "name", "application-name");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = HtmlTagRegex().Replace(decoded, " ");
        var cleaned = WhitespaceRegex().Replace(withoutTags, " ").Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= 80 ? cleaned : cleaned[..80].TrimEnd();
    }

    private static string? FindMetaContent(string html, string attributeName, string attributeValue)
    {
        foreach (Match match in MetaTagRegex().Matches(html))
        {
            var attributes = match.Groups[1].Value;
            if (!string.Equals(ReadAttribute(attributes, attributeName), attributeValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = ReadAttribute(attributes, "content");
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return null;
    }

    private static string? FindTitle(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadAttribute(string attributes, string attributeName)
    {
        var pattern = $"\\b{Regex.Escape(attributeName)}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))";
        var match = Regex.Match(attributes, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Success
            ? match.Groups[1].Value
            : match.Groups[2].Success
                ? match.Groups[2].Value
                : match.Groups[3].Value;
    }

    [GeneratedRegex("<meta\\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex("<title\\b[^>]*>(.*?)</title\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
