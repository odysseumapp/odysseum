using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Folders;

public interface IFolder : IItem
{
    FolderView? PinnedView { get; }
    /// <summary>The folder whose documents are the columns of this folder's grid. Null means the default.</summary>
    string? GridFolderId { get; }
    /// <summary>Subfolders and documents in order. This is the only source of the order.</summary>
    IReadOnlyList<IItem> Children { get; }
    /// <summary>The folder's own hidden document. It is never one of the children. The root has none.</summary>
    IDocument? OwnDocument { get; }
}
