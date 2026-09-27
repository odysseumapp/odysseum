using Odysseum.Abstractions.Exceptions;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models.Editing;

/// <summary>The working copy behind <see cref="FolderEditor"/> and <see cref="DocumentEditor"/>. It keeps the items,
/// the children of each folder, and which items an edit touched. <see cref="Build"/> makes the edited
/// <see cref="Project"/>; <see cref="Changes"/> lists the touched items from it.</summary>
internal sealed class ProjectDraft
{
    private readonly Dictionary<string, Folder> _folders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _parent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ownDocument = new(StringComparer.Ordinal);
    private readonly HashSet<string> _touched = new(StringComparer.Ordinal);
    private readonly List<string> _removedFolders = [];

    public ProjectDraft(Project project)
    {
        Project = project;
        foreach (var folder in project.Folders)
        {
            _folders[folder.Id] = folder;
            _children[folder.Id] = folder.Children.Select(child => child.Id).ToList();
            foreach (var child in folder.Children) _parent[child.Id] = folder.Id;
            if (folder.OwnDocument is { } own) _ownDocument[folder.Id] = own.Id;
        }
        foreach (var document in project.Documents) _documents[document.Id] = document;
    }

    public Project Project { get; }
    public string RootId => Project.Root.Id;
    public ProjectSettings? Settings { get; set; }

    public Folder Folder(string id) => _folders.GetValueOrDefault(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");

    public Document Document(string id) => _documents.GetValueOrDefault(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.");

    public Item Item(string id) => _folders.TryGetValue(id, out var folder) ? folder
        : _documents.TryGetValue(id, out var document) ? document
        : throw new WorkspaceException(WorkspaceError.NotFound, "That item no longer exists.");

    public bool HasFolder(string id) => _folders.ContainsKey(id);
    public bool HasDocument(string id) => _documents.ContainsKey(id);
    public string? ParentOf(string id) => _parent.GetValueOrDefault(id);
    public IReadOnlyList<string> ChildrenOf(string folderId) => _children.GetValueOrDefault(folderId) ?? [];
    public bool IsOwnDocument(string documentId) => _ownDocument.ContainsValue(documentId);

    public string PathOf(string id)
    {
        if (id == RootId) return "";
        var item = Item(id);
        var parent = ParentOf(id);
        if (parent is null)
        {
            var owner = _ownDocument.FirstOrDefault(pair => pair.Value == id).Key
                ?? throw new WorkspaceException(WorkspaceError.NotFound, "That item is not part of the project.");
            return Models.Item.Join(PathOf(owner), item.Name);
        }
        return Models.Item.Join(PathOf(parent), item.Name);
    }

    public bool IsDescendant(string folderId, string ancestorId)
    {
        for (var current = ParentOf(folderId); current is not null; current = ParentOf(current))
            if (current == ancestorId) return true;
        return false;
    }

    public void Put(Folder folder)
    {
        if (!_children.ContainsKey(folder.Id)) _children[folder.Id] = [];
        _folders[folder.Id] = folder;
        _touched.Add(folder.Id);
    }

    public void Put(Document document)
    {
        _documents[document.Id] = document;
        _touched.Add(document.Id);
    }

    public void Touch(string id) => _touched.Add(id);

    /// <summary>Places an item among a folder's children. The index is clamped to the list.</summary>
    public void Attach(string id, string folderId, int index)
    {
        var children = _children[folderId];
        children.Insert(Math.Clamp(index, 0, children.Count), id);
        _parent[id] = folderId;
        _touched.Add(folderId);
        _touched.Add(id);
    }

    public void Detach(string id)
    {
        if (!_parent.Remove(id, out var parent)) return;
        _children[parent].Remove(id);
        _touched.Add(parent);
    }

    public void RemoveFolder(string id)
    {
        Detach(id);
        _folders.Remove(id);
        _children.Remove(id);
        _touched.Remove(id);
        if (_ownDocument.Remove(id, out var own))
        {
            _documents.Remove(own);
            _touched.Remove(own);
        }
        _removedFolders.Add(id);
    }

    public Project Build()
    {
        var data = Project.Data with { Settings = Settings ?? Project.Settings, Root = BuildFolder(RootId) };
        return new Project(Project.Branch, data);
    }

    private Folder BuildFolder(string id)
    {
        var children = ChildrenOf(id).Select(child => _folders.ContainsKey(child) ? BuildFolder(child) : (Item)_documents[child]).ToArray();
        var own = _ownDocument.TryGetValue(id, out var ownId) ? _documents.GetValueOrDefault(ownId) : null;
        return _folders[id].WithChildren(children).WithOwnDocument(own);
    }

    public Changes Changes()
    {
        var built = Build();
        var folders = _touched.Where(_folders.ContainsKey).Select(id => built.Folder(id)!).OrderBy(f => f.Path.Count(c => c == '/')).ThenBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var documents = _touched.Where(_documents.ContainsKey).Select(id => built.Document(id)!).OrderBy(d => d.Path, StringComparer.Ordinal).ToArray();
        return new Changes(Settings, folders, documents, [.. _removedFolders]);
    }
}
