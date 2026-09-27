using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;
using Odysseum.Abstractions.Projects;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models;

/// <summary>A project at one moment. The constructor places every item: it sets <c>ParentId</c>, <c>OrderInParent</c>
/// and <c>Path</c> on a copy of each folder and document. This is the only place those are set.</summary>
public sealed class Project : IProject
{
    private readonly Dictionary<string, Item> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Folder> _foldersByPath = new(StringComparer.Ordinal);

    public Project(ProjectBranch branch, ProjectData data)
    {
        Branch = branch;
        Data = data;
        Root = Place(data.Root, null, 0, "");
        Folders = _foldersByPath.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        Documents = _items.Values.OfType<Document>().OrderBy(d => d.Path, StringComparer.Ordinal).ToArray();
    }

    private Folder Place(Folder folder, string? parentId, int orderInParent, string path)
    {
        var children = new Item[folder.Children.Count];
        for (var index = 0; index < children.Length; index++)
        {
            var child = folder.Children[index];
            var childPath = Join(path, child.Name);
            children[index] = child switch
            {
                Folder sub => Place(sub, folder.Id, index, childPath),
                Document document => Register(document.Placed(folder.Id, index, childPath)),
                _ => throw new InvalidOperationException("Unknown item type."),
            };
        }
        var own = folder.OwnDocument is { } ownSource ? Register(ownSource.Placed(folder.Id, -1, Join(path, ownSource.Name))) : null;
        var placed = folder.Placed(parentId, orderInParent, path, children, own);
        _items[placed.Id] = placed;
        _foldersByPath[path] = placed;
        return placed;
    }

    private Document Register(Document document)
    {
        _items[document.Id] = document;
        return document;
    }

    internal ProjectData Data { get; }
    internal ProjectSettings Settings => Data.Settings;
    public ProjectBranch Branch { get; }
    public string Name => Branch.Project;
    public string Id => Data.Id;
    public string Title => Settings.Title;
    public int WordGoal => Settings.WordGoal;
    public int DefaultSceneWordGoal => Settings.DefaultSceneWordGoal;
    public string Revision => Data.Revision;
    public string? Warning => Data.Warning;
    public Folder Root { get; }
    /// <summary>Every folder, the root first, then by path.</summary>
    public IReadOnlyList<Folder> Folders { get; }
    /// <summary>Every document, folders' own documents included, by path.</summary>
    public IReadOnlyList<Document> Documents { get; }
    public DateTimeOffset LastModified => Documents.Count == 0 ? default : Documents.Max(d => d.Modified);

    IFolder IProject.Root => Root;
    IFolder? IProject.Folder(string id) => Folder(id);
    IDocument? IProject.Document(string id) => Document(id);

    public Item? Item(string id) => _items.GetValueOrDefault(id);
    public Folder? Folder(string id) => _items.GetValueOrDefault(id) as Folder;
    public Document? Document(string id) => _items.GetValueOrDefault(id) as Document;
    public Folder? FolderAt(string path) => _foldersByPath.GetValueOrDefault(path.Trim('/'));
    public Folder? Parent(Item item) => item.ParentId is null ? null : Folder(item.ParentId);

    public string PathOf(IItem item) => (Item(item.Id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "That item is not part of this project.")).Path;

    /// <summary>Every document in manuscript order: a folder's own document first, then its children in order,
    /// descending into subfolders.</summary>
    public IEnumerable<Document> Walk() => Walk(Root);

    private static IEnumerable<Document> Walk(Folder folder)
    {
        if (folder.OwnDocument is { } own) yield return own;
        foreach (var child in folder.Children)
        {
            if (child is Document document) yield return document;
            else if (child is Folder sub) foreach (var nested in Walk(sub)) yield return nested;
        }
    }

    internal string Fingerprint() => Revision + "|" + Warning + "|" + string.Join(';',
        Documents.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id + d.Path + d.Revision));

    internal static string ParentPath(string path) => Models.Item.ParentPath(path);
    internal static string Join(string parentPath, string name) => Models.Item.Join(parentPath, name);
}
