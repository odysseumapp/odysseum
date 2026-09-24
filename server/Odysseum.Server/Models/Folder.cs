using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Repositories.Manifests;

namespace Odysseum.Server.Models;

public sealed class Folder : IFolder
{
    private readonly List<Folder> _folders = [];
    private readonly List<Document> _documents = [];

    internal Folder(Project project, Folder? parent, string path, string name, FolderManifest manifest)
    {
        Owner = project;
        ParentFolder = parent;
        Path = path;
        Name = name;
        Manifest = manifest;
        PinnedView = manifest.PinnedView switch
        {
            "write" => FolderView.Write,
            "board" => FolderView.Board,
            "outline" => FolderView.Outline,
            "grid" => FolderView.Grid,
            _ => null,
        };
        GridFolderId = manifest.GridFolder;
        ItemOrder = manifest.ItemOrder;
    }

    private Folder(Folder source, FolderView? pinnedView, string? gridFolderId, IReadOnlyList<string> itemOrder)
    {
        Owner = source.Owner;
        ParentFolder = source.ParentFolder;
        Path = source.Path;
        Name = source.Name;
        Manifest = source.Manifest;
        _folders = source._folders;
        _documents = source._documents;
        OwnDocument = source.OwnDocument;
        PinnedView = pinnedView;
        GridFolderId = gridFolderId;
        ItemOrder = itemOrder;
    }

    internal Project Owner { get; }
    internal FolderManifest Manifest { get; }
    public string Id => Manifest.Id;
    public IProject Project => Owner;
    public IFolder? Parent => ParentFolder;
    public Folder? ParentFolder { get; }
    public string Name { get; }
    public string Path { get; }
    public bool IsRoot => ParentFolder is null;
    public FolderView? PinnedView { get; }
    internal string? GridFolderId { get; }
    public IFolder? GridFolder => GridFolderId is null ? null : Owner.Folder(GridFolderId);
    internal IReadOnlyList<string> ItemOrder { get; }
    public IReadOnlyList<IFolder> Folders => _folders;
    public IReadOnlyList<IDocument> Documents => _documents;
    public IReadOnlyList<Folder> ChildFolders => _folders;
    public IReadOnlyList<Document> ChildDocuments => _documents;
    public Document? OwnDocument { get; private set; }

    internal void Add(Folder folder) => _folders.Add(folder);

    internal void Add(Document document)
    {
        _documents.Add(document);
        if (document.IsFolderDocument) OwnDocument = document;
    }

    internal Folder With(FolderLayout layout) => new(this, layout.PinnedView, layout.GridFolderId, ItemOrder);
    internal Folder WithItemOrder(IReadOnlyList<string> itemOrder) => new(this, PinnedView, GridFolderId, itemOrder);

    internal string ChildPath(string name) => IsRoot ? name : Path + "/" + name;
}
