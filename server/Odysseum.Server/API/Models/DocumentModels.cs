using Odysseum.Server.API.Enums;

namespace Odysseum.Server.API.Models;

/// <summary>Everything about a document except its prose. <c>Revision</c> is the SHA-256 of the file on disk.</summary>
public record DocumentSummary(string Id, string Path, string Title, string Folder,
    string Synopsis, string Notes, DocumentStatus Status, int WordGoal, double Order,
    int WordCount, string Revision, DateTime LastModified, DocumentKind Kind, IReadOnlyList<string> Links,
    IReadOnlyDictionary<string, string> LinkNotes);

public record DocumentContent(DocumentSummary Document, string Content);

public record CreateDocumentRequest(string Title, string Folder, string? Content);

/// <summary>The server rejects the save with 409 when <c>Revision</c> no longer matches the file on disk.</summary>
public record SaveDocumentRequest(string Content, string Revision);

/// <summary><c>Links</c> is the complete set of documents this one is linked to. Links are undirected, so the server
/// updates the other documents too. Omitting it leaves links unchanged. <c>LinkNotes</c> is the complete set of notes
/// on this document's links, keyed by the linked document. Both ends of a link share its note.</summary>
public record MetadataRequest(string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, string Revision, string[]? Links = null,
    Dictionary<string, string>? LinkNotes = null);

public record MoveDocumentRequest(string Path, string Revision);

public record SnapshotInfo(string Id, DateTime Created, int WordCount);

public record SnapshotContent(string Content);

public record SearchResult(DocumentSummary Document, string Excerpt);
