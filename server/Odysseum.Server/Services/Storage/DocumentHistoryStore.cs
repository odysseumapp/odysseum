using System.Text.RegularExpressions;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Services.Storage;

/// <summary>Readable recovery snapshots, deduplicated by content hash. Called under the project's lock.</summary>
internal sealed partial class DocumentHistoryStore(ProjectFileStore files)
{
    public async Task SaveAsync(string id, byte[] bytes)
    {
        var folder = $".writer/history/{id}";
        var hash = ContentRevision.Hash(bytes);
        if (files.EnumerateMetadataFiles(folder, $"*-{hash}.md").Any()) return;
        var name = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{hash}.md";
        await files.WriteAsync($"{folder}/{name}", bytes, overwrite: false, metadata: true);
    }

    public async Task<IReadOnlyList<SnapshotInfo>> ListAsync(string id)
    {
        var result = new List<SnapshotInfo>();
        foreach (var path in files.EnumerateMetadataFiles($".writer/history/{id}", "*.md").OrderDescending().Take(100))
        {
            var content = MarkdownDocumentCodec.Decode(await files.ReadAsync(path, metadata: true));
            result.Add(new(Path.GetFileNameWithoutExtension(path), files.LastModified(path, metadata: true),
                MarkdownDocumentCodec.CountWords(MarkdownDocumentCodec.Split(content).Body)));
        }
        return result;
    }

    public async Task<string> ReadAsync(string id, string snapshot)
    {
        if (!SnapshotName().IsMatch(snapshot)) throw new WorkspaceException(400, "Invalid snapshot.");
        var text = MarkdownDocumentCodec.Decode(await files.ReadAsync($".writer/history/{id}/{snapshot}.md", metadata: true));
        return MarkdownDocumentCodec.Split(text).Body;
    }

    [GeneratedRegex(@"^\d{8}T\d{13}-[a-f0-9]{64}$")] private static partial Regex SnapshotName();
}
