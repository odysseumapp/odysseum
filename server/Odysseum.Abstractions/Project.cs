namespace Odysseum.Abstractions;

/// <summary>Everything the store knows about a project at one moment. Immutable; callers derive a changed copy with
/// <c>with</c> and hand it to <see cref="IProjectStore.CommitAsync"/>.</summary>
public sealed record Project
{
    /// <summary>The root folder. It is also the project's identity.</summary>
    public required FolderId Root { get; init; }
    public required ProjectSettings Settings { get; init; }
    /// <summary>Changes whenever any metadata changes. Passed back on commit so a stale write is refused.</summary>
    public required string Revision { get; init; }
    public required IReadOnlyDictionary<DocumentId, Document> Documents { get; init; }
    /// <summary>Every folder, the root included.</summary>
    public required IReadOnlyDictionary<FolderId, Folder> Folders { get; init; }
    /// <summary>Something the writer should know about the last load, such as a file that could not be read.</summary>
    public string? Warning { get; init; }
}
