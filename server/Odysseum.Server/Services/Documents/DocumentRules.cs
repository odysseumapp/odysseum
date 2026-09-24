using System.Text.RegularExpressions;
using Odysseum.Server.API.Enums;

namespace Odysseum.Server.Services.Documents;

internal static partial class DocumentRules
{
    public static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new WorkspaceException(400, "A title is required.");
        title = title.Trim();
        if (title.Length is 0 or > 200) throw new WorkspaceException(400, "Use a title between 1 and 200 characters.");
        return title;
    }
    public static string FileName(string title)
    {
        var result = UnsafeName().Replace(title, "-").Trim(' ', '.', '-');
        if (string.IsNullOrEmpty(result)) result = "Untitled";
        if (ReservedName().IsMatch(result)) result = "Scene-" + result;
        return result;
    }
    public static DocumentKind KindOf(string path) => path.Split('/')[0].ToLowerInvariant() switch
    {
        "characters" => DocumentKind.Character,
        "locations" => DocumentKind.Location,
        "threads" => DocumentKind.Thread,
        "notes" or "research" or "story notes" => DocumentKind.Note,
        "styles" => DocumentKind.Style,
        _ => DocumentKind.Scene,
    };

    public const string StyleStarter = "```css\n/* Only .name blocks count. .normal is what untagged text looks like;\n"
        + "   every other .name is a style to pick from the toolbar. */\n.normal {\n}\n```\n";
    public static string StarterContent(string path) => KindOf(path) == DocumentKind.Style ? StyleStarter : "";

    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".txt";
    public static string FolderDocumentPath(string folder) => $"{folder}/.{Path.GetFileName(folder)}.md";
    public static bool IsFolderDocument(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 2 && parts[^1] == $".{parts[^2]}.md";
    }
    [GeneratedRegex("[<>:\"/\\\\|?*\\x00-\\x1f]")] private static partial Regex UnsafeName();
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)] private static partial Regex ReservedName();
}
