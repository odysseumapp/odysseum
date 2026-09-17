using Odysseum.Server.API.Enums;

namespace Odysseum.Server.API.Models;

/// <summary>Everything about a document except its prose. <c>Revision</c> is the SHA-256 of the file on disk.</summary>
public record DocumentSummary(string Id, string Path, string Title, string Folder,
    string Synopsis, string Notes, DocumentStatus Status, int WordGoal, double Order,
    int WordCount, string Revision, DateTime LastModified, DocumentKind Kind, IReadOnlyList<string> Links,
    IReadOnlyDictionary<string, string> LinkNotes);

public record DocumentContent(DocumentSummary Document, string Content);

public record CreateDocumentRequest(string Title, string Folder, string? Content);

/// <summary>Saves are rejected with 409 when <c>Revision</c> no longer matches the file on disk.</summary>
public record SaveDocumentRequest(string Content, string Revision);

/// <summary><c>Links</c> is the complete set of documents this one is linked to (links are undirected, so the other
/// documents are updated too). Omitting it leaves links unchanged. <c>LinkNotes</c> is likewise the complete set of
/// notes on this document's links, keyed by the linked document; a note is shared by both ends of its link.</summary>
public record MetadataRequest(string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, string Revision, string[]? Links = null,
    Dictionary<string, string>? LinkNotes = null);

public record MoveDocumentRequest(string Path, string Revision);

public record SnapshotInfo(string Id, DateTime Created, int WordCount);

public record SnapshotContent(string Content);

public record SearchResult(DocumentSummary Document, string Excerpt);
