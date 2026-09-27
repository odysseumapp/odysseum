using System.Text.Json;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Views;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models.Editing;

/// <summary>Edits the folders of a copy of a project and marks what changed. <see cref="Result"/> is the edited
/// project; <see cref="Changes"/> is what storage must save.</summary>
public sealed class FolderEditor
{
    private readonly ProjectDraft _draft;

    public FolderEditor(Project project) : this(new ProjectDraft(project)) { }

    internal FolderEditor(ProjectDraft draft)
    {
        _draft = draft;
    }

    /// <summary>A document editor on the same working copy.</summary>
    public DocumentEditor Documents => new(_draft);

    public Folder Create(string parentId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || !FileManager.IsSafePath(name))
            throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        _draft.Folder(parentId);
        if (_draft.ChildrenOf(parentId).Any(id => _draft.HasFolder(id) && string.Equals(_draft.Folder(id).Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkspaceException(WorkspaceError.Conflict, "A folder already has that name.");
        var folder = new Folder(Guid.NewGuid().ToString(), name);
        _draft.Put(folder);
        _draft.Attach(folder.Id, parentId, int.MaxValue);
        return folder;
    }

    /// <summary>Puts a folder or document at <c>index</c> among the target folder's children. The index is clamped.</summary>
    public void Move(string itemId, string targetFolderId, int index)
    {
        var item = _draft.Item(itemId);
        _draft.Folder(targetFolderId);
        if (item is Folder)
        {
            if (itemId == _draft.RootId) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be moved.");
            if (itemId == targetFolderId || _draft.IsDescendant(targetFolderId, itemId))
                throw new WorkspaceException(WorkspaceError.Invalid, "A folder cannot move into itself.");
        }
        else if (_draft.IsOwnDocument(itemId))
            throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder.");
        _draft.Detach(itemId);
        _draft.Attach(itemId, targetFolderId, index);
    }

    /// <summary>Sets the pinned view and changes the settings of the views named in the layout. View names and settings
    /// are not checked against any list of views.</summary>
    public void SetLayout(string folderId, FolderLayout layout)
    {
        var folder = _draft.Folder(folderId);
        if (layout.PinnedView is not null) ViewNames.Check(layout.PinnedView);
        var views = new Dictionary<string, JsonElement>(folder.Views, StringComparer.Ordinal);
        foreach (var (name, settings) in layout.Views ?? new Dictionary<string, JsonElement>())
        {
            ViewNames.Check(name);
            if (ViewNames.Removes(settings)) views.Remove(name);
            else views[name] = settings.Clone();
        }
        ViewNames.CheckSettings(views);
        _draft.Put(folder.WithLayout(layout.PinnedView, views));
    }

    /// <summary>Removes a folder that has no children. Its own hidden document goes with it.</summary>
    public void Remove(string folderId)
    {
        _draft.Folder(folderId);
        if (folderId == _draft.RootId) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be removed.");
        if (_draft.ChildrenOf(folderId).Count > 0)
            throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be removed. Move their files and subfolders first.");
        _draft.RemoveFolder(folderId);
    }

    public void SetSettings(ProjectSettings settings) => _draft.Settings = settings;

    public Project Result() => _draft.Build();

    public Changes Changes() => _draft.Changes();
}
