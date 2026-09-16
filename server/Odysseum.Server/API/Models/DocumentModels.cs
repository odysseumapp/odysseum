using Odysseum.Server.API.Enums;

namespace Odysseum.Server.API.Models;

/// <summary>Everything about a document except its prose. <c>Revision</c> is the SHA-256 of the file on disk.</summary>
public record DocumentSummary(string Id, string Path, string Title, string Folder,
    string Synopsis, string Notes, DocumentStatus Status, int WordGoal, double Order,
    int WordCount, string Revision, DateTime LastModified, DocumentKind Kind, IReadOnlyList<string> Characters, IReadOnlyList<string> Locations, IReadOnlyList<string> Threads);

public record DocumentContent(DocumentSummary Document, string Content);

public record CreateDocumentRequest(string Title, string Folder, string? Content);

/// <summary>Saves are rejected with 409 when <c>Revision</c> no longer matches the file on disk.</summary>
public record SaveDocumentRequest(string Content, string Revision);

/// <summary><c>Characters</c>, <c>Locations</c>, and <c>Threads</c> list linked IDs; a thread document cannot itself be on a thread. Omitted attachments remain unchanged.</summary>
public record MetadataRequest(string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, string Revision, string[]? Characters = null, string[]? Locations = null, string[]? Threads = null);

public record MoveDocumentRequest(string Path, string Revision);

public record SnapshotInfo(string Id, DateTime Created, int WordCount);

public record SnapshotContent(string Content);

public record SearchResult(DocumentSummary Document, string Excerpt);
