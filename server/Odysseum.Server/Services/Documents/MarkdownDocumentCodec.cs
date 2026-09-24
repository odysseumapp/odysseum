using System.Text;
using System.Text.RegularExpressions;

namespace Odysseum.Server.Services.Documents;

internal static partial class MarkdownDocumentCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Decode(byte[] bytes) => Utf8.GetString(bytes);
    public static byte[] Encode(string prefix, string body)
    {
        var bytes = Utf8.GetBytes(prefix + body);
        if (bytes.Length > Storage.ProjectFileStore.MaxFileBytes)
            throw new WorkspaceException(413, "Documents must be smaller than 4 MB.");
        return bytes;
    }

    public static int CountWords(string content) => Word().Matches(content).Count;
    public static (string Prefix, string Body, string? Id) Split(string text)
    {
        var match = Frontmatter().Match(text);
        if (!match.Success) return (text.StartsWith('\uFEFF') ? "\uFEFF" : "", text.TrimStart('\uFEFF'), null);
        var idMatch = WriterId().Match(match.Value);
        string? id = idMatch.Success && Guid.TryParse(idMatch.Groups[1].Value, out var guid) ? guid.ToString() : null;
        return (match.Value, text[match.Length..], id);
    }
    [GeneratedRegex(@"\A\uFEFF?---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)(?:\r?\n)?")] private static partial Regex Frontmatter();
    [GeneratedRegex("(?m)^writer_id:[ \\t]*[\"']?([0-9a-fA-F-]{36})[\"']?[ \\t]*\\r?$")] private static partial Regex WriterId();
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")] private static partial Regex Word();
}
