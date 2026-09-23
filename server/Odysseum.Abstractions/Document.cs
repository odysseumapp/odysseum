namespace Odysseum.Abstractions;

/// <summary>A document as the store last saw it: its metadata and its prose. Nothing here says where or how it is kept.</summary>
public sealed record Document
{
    public required DocumentId Id { get; init; }
    public required FolderId Folder { get; init; }
    public required DocumentKind Kind { get; init; }
    /// <summary>Where the store keeps it, for showing to the writer. Opaque: callers never parse or build one.</summary>
    public required string Location { get; init; }

    public string Title { get; init; } = "";
    public string Synopsis { get; init; } = "";
    public string Notes { get; init; } = "";
    public DocumentStatus Status { get; init; } = DocumentStatus.Draft;
    public int WordGoal { get; init; } = 1000;
    /// <summary>Position in the manuscript. Ties break on <see cref="Location"/>.</summary>
    public double Order { get; init; }
    /// <summary>Documents this one is linked to. Links are undirected; the store keeps both sides listed.</summary>
    public IReadOnlyList<DocumentId> Links { get; init; } = [];
    /// <summary>An optional note on a link, keyed by the other document.</summary>
    public IReadOnlyDictionary<DocumentId, string> LinkNotes { get; init; } = new Dictionary<DocumentId, string>();

    /// <summary>The editable prose. Anything the store wraps around it (frontmatter, encoding) is the store's business.</summary>
    public string Body { get; init; } = "";
    /// <summary>Changes whenever <see cref="Body"/> changes. Passed back on save so a stale write is refused.</summary>
    public required string Revision { get; init; }
    public DateTimeOffset Modified { get; init; }
}
