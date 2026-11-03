using System.Net;
using System.Text.RegularExpressions;

namespace VSModManager.Services;

/// <summary>Turns the ModDB's simple changelog HTML into readable plain text.</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<\s*li[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex ListItem();
    [GeneratedRegex(@"<\s*(br|/p|/li|/h\d|/div|/ul|/ol)\s*/?\s*>", RegexOptions.IgnoreCase)] private static partial Regex LineBreak();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex AnyTag();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex ExtraBlankLines();
    [GeneratedRegex(@"\n{2,}• ")] private static partial Regex BlankBeforeBullet();

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var s = html.Replace("\r", "").Replace("\n", " ");
        s = ListItem().Replace(s, "\n• ");
        s = LineBreak().Replace(s, "\n");
        s = AnyTag().Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
        s = Spaces().Replace(s, " ");
        s = string.Join("\n", s.Split('\n').Select(l => l.Trim()));
        s = BlankBeforeBullet().Replace(s, "\n• ");
        return ExtraBlankLines().Replace(s, "\n\n").Trim();
    }
}
