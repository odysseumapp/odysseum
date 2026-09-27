using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;

namespace Odysseum.Server.Models;

public sealed class Folder : Item, IFolder
{
    public Folder(string id, string name, FolderView? pinnedView = null, string? gridFolderId = null,
        IReadOnlyList<Item>? children = null, Document? ownDocument = null)
        : this(id, name, null, -1, name, pinnedView, gridFolderId, children ?? [], ownDocument) { }

    private Folder(string id, string name, string? parentId, int orderInParent, string path,
        FolderView? pinnedView, string? gridFolderId, IReadOnlyList<Item> children, Document? ownDocument)
        : base(id, name, parentId, orderInParent, path)
    {
        PinnedView = pinnedView;
        GridFolderId = gridFolderId;
        Children = children;
        OwnDocument = ownDocument;
    }

    public FolderView? PinnedView { get; }
    public string? GridFolderId { get; }
    /// <summary>Subfolders and documents in order. The folder's own hidden document is never listed here.</summary>
    public IReadOnlyList<Item> Children { get; }
    public Document? OwnDocument { get; }
    public bool IsRoot => ParentId is null && OrderInParent >= 0;
    public IEnumerable<Folder> Folders => Children.OfType<Folder>();
    public IEnumerable<Document> Documents => Children.OfType<Document>();

    IReadOnlyList<IItem> IFolder.Children => Children;
    IDocument? IFolder.OwnDocument => OwnDocument;

    internal Folder Placed(string? parentId, int orderInParent, string path, IReadOnlyList<Item> children, Document? ownDocument) =>
        new(Id, Name, parentId, orderInParent, path, PinnedView, GridFolderId, children, ownDocument);

    internal Folder WithChildren(IReadOnlyList<Item> children) =>
        new(Id, Name, ParentId, OrderInParent, Path, PinnedView, GridFolderId, children, OwnDocument);

    internal Folder WithOwnDocument(Document? ownDocument) =>
        new(Id, Name, ParentId, OrderInParent, Path, PinnedView, GridFolderId, Children, ownDocument);

    internal Folder WithLayout(FolderView? pinnedView, string? gridFolderId) =>
        new(Id, Name, ParentId, OrderInParent, Path, pinnedView, gridFolderId, Children, OwnDocument);

    internal Folder WithName(string name) =>
        new(Id, name, ParentId, OrderInParent, Path, PinnedView, GridFolderId, Children, OwnDocument);
}
