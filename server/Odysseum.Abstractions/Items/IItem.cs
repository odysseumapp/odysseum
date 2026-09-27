namespace Odysseum.Abstractions.Items;

/// <summary>One entry in a project tree: a folder or a document. <c>ParentId</c> and <c>OrderInParent</c> are
/// set by the project when it builds the tree; the root folder has no parent.</summary>
public interface IItem
{
    string Id { get; }
    /// <summary>The folder name, or the document's file name with its extension.</summary>
    string Name { get; }
    string? ParentId { get; }
    int OrderInParent { get; }
}
