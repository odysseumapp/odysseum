namespace Odysseum.Abstractions;

public sealed record Document
{
    public required DocumentId Id { get; init; }
    public required FolderId Folder { get; init; }
    public required DocumentKind Kind { get; init; }
    public required string Location { get; init; }

    public string Title { get; init; } = "";
    public string Synopsis { get; init; } = "";
    public string Notes { get; init; } = "";
    public DocumentStatus Status { get; init; } = DocumentStatus.Draft;
    public int WordGoal { get; init; } = 1000;
    public double Order { get; init; }
    public IReadOnlyList<DocumentId> Links { get; init; } = [];
    public IReadOnlyDictionary<DocumentId, string> LinkNotes { get; init; } = new Dictionary<DocumentId, string>();

    public string Body { get; init; } = "";
    public required string Revision { get; init; }
    public DateTimeOffset Modified { get; init; }
}
