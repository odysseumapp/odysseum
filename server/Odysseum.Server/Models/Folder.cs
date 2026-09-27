using System.Text.Json;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;

namespace Odysseum.Server.Models;

public sealed class Folder : Item, IFolder
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoViews = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public Folder(string id, string name, string? pinnedView = null, IReadOnlyDictionary<string, JsonElement>? views = null,
        IReadOnlyList<Item>? children = null, Document? ownDocument = null)
        : this(id, name, null, -1, name, pinnedView, views ?? NoViews, children ?? [], ownDocument) { }

    private Folder(string id, string name, string? parentId, int orderInParent, string path,
        string? pinnedView, IReadOnlyDictionary<string, JsonElement> views, IReadOnlyList<Item> children, Document? ownDocument)
        : base(id, name, parentId, orderInParent, path)
    {
        PinnedView = pinnedView;
        Views = views;
        Children = children;
        OwnDocument = ownDocument;
    }

    public string? PinnedView { get; }
    public IReadOnlyDictionary<string, JsonElement> Views { get; }
    /// <summary>Subfolders and documents in order. The folder's own hidden document is never listed here.</summary>
    public IReadOnlyList<Item> Children { get; }
    public Document? OwnDocument { get; }
    public bool IsRoot => ParentId is null && OrderInParent >= 0;
    public IEnumerable<Folder> Folders => Children.OfType<Folder>();
    public IEnumerable<Document> Documents => Children.OfType<Document>();

    IReadOnlyList<IItem> IFolder.Children => Children;
    IDocument? IFolder.OwnDocument => OwnDocument;

    internal Folder Placed(string? parentId, int orderInParent, string path, IReadOnlyList<Item> children, Document? ownDocument) =>
        new(Id, Name, parentId, orderInParent, path, PinnedView, Views, children, ownDocument);

    internal Folder WithChildren(IReadOnlyList<Item> children) =>
        new(Id, Name, ParentId, OrderInParent, Path, PinnedView, Views, children, OwnDocument);

    internal Folder WithOwnDocument(Document? ownDocument) =>
        new(Id, Name, ParentId, OrderInParent, Path, PinnedView, Views, Children, ownDocument);

    internal Folder WithLayout(string? pinnedView, IReadOnlyDictionary<string, JsonElement> views) =>
        new(Id, Name, ParentId, OrderInParent, Path, pinnedView, views, Children, OwnDocument);

    internal Folder WithName(string name) =>
        new(Id, name, ParentId, OrderInParent, Path, PinnedView, Views, Children, OwnDocument);
}
