using System.Text.RegularExpressions;
using Odysseum.Server.API.Enums;

namespace Odysseum.Server.Services.Documents;

/// <summary>Document classification and naming rules shared by project and document creation.</summary>
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
    /// <summary>The top-level folder decides what a document is; every kind shares the same file and metadata handling.</summary>
    public static DocumentKind KindOf(string path) => path.Split('/')[0].ToLowerInvariant() switch
    {
        "characters" => DocumentKind.Character,
        "locations" => DocumentKind.Location,
        "arcs" => DocumentKind.Arc,
        "notes" or "research" or "story notes" => DocumentKind.Note,
        _ => DocumentKind.Scene,
    };

    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".txt";
    [GeneratedRegex("[<>:\"/\\\\|?*\\x00-\\x1f]")] private static partial Regex UnsafeName();
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)] private static partial Regex ReservedName();
}
