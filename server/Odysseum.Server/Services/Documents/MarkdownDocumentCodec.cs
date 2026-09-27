using Odysseum.Abstractions.Exceptions;
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
        if (bytes.Length > Repositories.Files.FileManager.MaxFileBytes)
            throw new WorkspaceException(WorkspaceError.TooLarge, "Documents must be smaller than 4 MB.");
        return bytes;
    }

    public static int CountWords(string content) => Word().Matches(content).Count;

    /// <summary>Splits off a front matter block that another program wrote at the top of the file. Odysseum does not
    /// read it; it keeps it unchanged when it saves the text.</summary>
    public static (string Prefix, string Body) Split(string text)
    {
        var match = Frontmatter().Match(text);
        if (!match.Success) return (text.StartsWith('﻿') ? "﻿" : "", text.TrimStart('﻿'));
        return (match.Value, text[match.Length..]);
    }
    [GeneratedRegex(@"\A﻿?---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)(?:\r?\n)?")] private static partial Regex Frontmatter();
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")] private static partial Regex Word();
}
