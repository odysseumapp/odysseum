using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Repositories.Files;
using System.Text.RegularExpressions;
using Odysseum.Server.Models;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Repositories;

public sealed partial class DocumentVersionRepository(IFileManager files) : IDocumentVersionRepository
{
    public async Task SaveAsync(string id, byte[] bytes)
    {
        var folder = $".odysseum/history/{id}";
        var hash = ContentRevision.Hash(bytes);
        if (files.EnumerateFiles(folder, $"*-{hash}.md", metadata: true).Any()) return;
        var name = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{hash}.md";
        await files.WriteAsync($"{folder}/{name}", bytes, overwrite: false, metadata: true);
    }

    public Task<IReadOnlyList<DocumentVersion>> ListAsync(Document document)
    {
        var versions = files.EnumerateFiles($".odysseum/history/{document.Id}", "*.md", metadata: true).OrderDescending().Take(100)
            .Select(path => Path.GetFileNameWithoutExtension(path) is var name
                ? new DocumentVersion(document, name, new DateTimeOffset(files.LastModified(path, metadata: true), TimeSpan.Zero), () => ReadAsync(document.Id, name))
                : throw new InvalidOperationException())
            .ToArray();
        return Task.FromResult<IReadOnlyList<DocumentVersion>>(versions);
    }

    public async Task<string> ReadAsync(string id, string snapshot)
    {
        if (!SnapshotName().IsMatch(snapshot)) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid snapshot.");
        var text = MarkdownDocumentCodec.Decode(await files.ReadAsync($".odysseum/history/{id}/{snapshot}.md", metadata: true));
        return MarkdownDocumentCodec.Split(text).Body;
    }

    [GeneratedRegex(@"^\d{8}T\d{13}-[a-f0-9]{64}$")] private static partial Regex SnapshotName();
}
