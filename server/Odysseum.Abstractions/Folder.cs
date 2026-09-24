namespace Odysseum.Abstractions;

public sealed record Folder
{
    public required FolderId Id { get; init; }
    public FolderId? Parent { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public double Order { get; init; }

    public FolderView? PinnedView { get; init; }
    public IReadOnlyList<OrderedItem> ItemOrder { get; init; } = [];
    public FolderId? GridFolder { get; init; }
}
