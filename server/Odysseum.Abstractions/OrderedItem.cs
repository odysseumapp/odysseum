namespace Odysseum.Abstractions;

public readonly record struct OrderedItem
{
    public DocumentId? Document { get; }
    public FolderId? Folder { get; }
    private OrderedItem(DocumentId? document, FolderId? folder) { Document = document; Folder = folder; }
    public static OrderedItem Of(DocumentId id) => new(id, null);
    public static OrderedItem Of(FolderId id) => new(null, id);
    public override string ToString() => Document?.Value ?? Folder!.Value.Value;
}
