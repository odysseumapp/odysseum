namespace Odysseum.Abstractions;

/// <summary>A folder in the project tree and how it is laid out. The root has no parent.</summary>
public sealed record Folder
{
    public required FolderId Id { get; init; }
    public FolderId? Parent { get; init; }
    public required string Name { get; init; }
    /// <summary>Where the store keeps it, for showing to the writer. Opaque: callers never parse or build one.</summary>
    public required string Location { get; init; }
    /// <summary>Position among its siblings.</summary>
    public double Order { get; init; }

    public FolderView? PinnedView { get; init; }
    /// <summary>The writer's arrangement of this folder's immediate children. Items missing here sort after the listed ones.</summary>
    public IReadOnlyList<OrderedItem> ItemOrder { get; init; } = [];
    /// <summary>The folder whose documents are the columns of this folder's grid; null picks a default.</summary>
    public FolderId? GridFolder { get; init; }
}
