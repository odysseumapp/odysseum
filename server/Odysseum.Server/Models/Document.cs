using Odysseum.Abstractions.Documents;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Models;

public sealed class Document : Item, IDocument
{
    public Document(string id, string name, string title, string synopsis = "", string notes = "",
        DocumentStatus status = DocumentStatus.Draft, int wordGoal = 0, IReadOnlyList<string>? links = null,
        IReadOnlyDictionary<string, string>? linkNotes = null, string revision = "", DateTimeOffset modified = default,
        int wordCount = 0, string? body = null)
        : this(id, name, null, -1, name, DocumentKind.Scene, false, title, synopsis, notes, status, wordGoal,
            links ?? [], linkNotes ?? new Dictionary<string, string>(StringComparer.Ordinal), revision, modified, wordCount, body) { }

    private Document(string id, string name, string? parentId, int orderInParent, string path, DocumentKind kind, bool isFolderDocument,
        string title, string synopsis, string notes, DocumentStatus status, int wordGoal, IReadOnlyList<string> links,
        IReadOnlyDictionary<string, string> linkNotes, string revision, DateTimeOffset modified, int wordCount, string? body)
        : base(id, name, parentId, orderInParent, path)
    {
        Kind = kind;
        IsFolderDocument = isFolderDocument;
        Title = title;
        Synopsis = synopsis;
        Notes = notes;
        Status = status;
        WordGoal = wordGoal;
        Links = links;
        LinkNotes = linkNotes;
        Revision = revision;
        Modified = modified;
        WordCount = wordCount;
        Body = body;
    }

    /// <summary>Set from the path when the project places the document.</summary>
    public DocumentKind Kind { get; }
    public bool IsFolderDocument { get; }
    public string Title { get; }
    public string Synopsis { get; }
    public string Notes { get; }
    public DocumentStatus Status { get; }
    public int WordGoal { get; }
    public IReadOnlyList<string> Links { get; }
    public IReadOnlyDictionary<string, string> LinkNotes { get; }
    public string Revision { get; }
    public DateTimeOffset Modified { get; }
    public int WordCount { get; }
    /// <summary>Null until the document is opened.</summary>
    public string? Body { get; }
    public string FileName => Name;

    internal Document Placed(string? parentId, int orderInParent, string path) =>
        new(Id, Name, parentId, orderInParent, path, DocumentRules.KindOf(path), DocumentRules.IsFolderDocument(path),
            Title, Synopsis, Notes, Status, WordGoal, Links, LinkNotes, Revision, Modified, WordCount, Body);

    internal Document WithBody(string body) =>
        new(Id, Name, ParentId, OrderInParent, Path, Kind, IsFolderDocument, Title, Synopsis, Notes, Status, WordGoal,
            Links, LinkNotes, Revision, Modified, MarkdownDocumentCodec.CountWords(body), body);

    internal Document WithDetails(DocumentDetails details) =>
        new(Id, Name, ParentId, OrderInParent, Path, Kind, IsFolderDocument, details.Title ?? Title, details.Synopsis ?? Synopsis,
            details.Notes ?? Notes, details.Status ?? Status, details.WordGoal ?? WordGoal, Links, LinkNotes, Revision, Modified, WordCount, Body);

    internal Document WithLinks(IReadOnlyList<string> links, IReadOnlyDictionary<string, string> linkNotes) =>
        new(Id, Name, ParentId, OrderInParent, Path, Kind, IsFolderDocument, Title, Synopsis, Notes, Status, WordGoal,
            links, linkNotes, Revision, Modified, WordCount, Body);

    internal Document WithName(string name) =>
        new(Id, name, ParentId, OrderInParent, Path, Kind, IsFolderDocument, Title, Synopsis, Notes, Status, WordGoal,
            Links, LinkNotes, Revision, Modified, WordCount, Body);
}
