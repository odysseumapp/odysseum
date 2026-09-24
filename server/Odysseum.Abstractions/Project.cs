namespace Odysseum.Abstractions;

public sealed record Project
{
    public required FolderId Root { get; init; }
    public required ProjectSettings Settings { get; init; }
    public required string Revision { get; init; }
    public required IReadOnlyDictionary<DocumentId, Document> Documents { get; init; }
    public required IReadOnlyDictionary<FolderId, Folder> Folders { get; init; }
    public string? Warning { get; init; }
}
