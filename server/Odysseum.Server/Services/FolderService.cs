using System.Text.Json;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Folders.Events;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class FolderService : IFolderService
{
    private readonly IFolderRepository _folders;
    private readonly IFolderPlaceRepository _folderPlaces;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentPlaceRepository _documentPlaces;
    private readonly IProjectLock _projectLock;
    private readonly ISettingsProvider? _settings;
    private readonly ViewCatalog _views;

    public FolderService(IFolderRepository folders, IFolderPlaceRepository folderPlaces, IDocumentRepository documents,
        IDocumentPlaceRepository documentPlaces, IProjectLock projectLock, ISettingsProvider? settings = null, ViewCatalog? views = null)
    {
        _folders = folders;
        _folderPlaces = folderPlaces;
        _documents = documents;
        _documentPlaces = documentPlaces;
        _projectLock = projectLock;
        _settings = settings;
        _views = views ?? ViewCatalog.Default;
        folders.ItemAdded += (_, change) => FolderCreated?.Invoke(this, new(change.Item));
        folders.ItemUpdated += (_, change) =>
        {
            if (change.Before?.ParentFolderId != change.Item.ParentFolderId) FolderMoved?.Invoke(this, new(change.Item));
            else FolderUpdated?.Invoke(this, new(change.Item));
        };
        folders.ItemRemoved += (_, change) => FolderRemoved?.Invoke(this, new(change.Item));
    }

    public event EventHandler<FolderEventArgs>? FolderCreated;
    public event EventHandler<FolderEventArgs>? FolderUpdated;
    public event EventHandler<FolderEventArgs>? FolderMoved;
    public event EventHandler<FolderEventArgs>? FolderRemoved;

    public async Task<IFolder> GetFolderByIdAsync(string folderId) => await FindAsync(folderId);

    public async Task<IReadOnlyList<IFolder>> GetFoldersByProjectIdAsync(string projectId) => await _folders.GetFoldersByProjectIdAsync(projectId);

    /// <summary>Saves the folder's place, then the folder, then its own hidden document, then adds the folder at the end of
    /// the parent folder's children.</summary>
    public async Task<IFolder> CreateFolderAsync(string parentFolderId, string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name.Contains('/') || !FileManager.IsSafePath(name))
            throw new WorkspaceException(WorkspaceError.Invalid, "That folder name is not allowed.");
        var parent = await FindAsync(parentFolderId);
        return await _projectLock.RunLockedAsync(parent.ProjectId, async () =>
        {
            var projectId = parent.ProjectId;
            var path = ProjectPaths.Join(await _folderPlaces.GetPathByFolderIdAsync(parent.Id), name);
            var id = Guid.NewGuid().ToString();
            await _folderPlaces.AddAsync(new FolderPlace(id, projectId, path, ""));
            await _folders.AddAsync(new Folder(id, projectId, name, parent.Id, [], null, null, new Dictionary<string, JsonElement>(StringComparer.Ordinal), ""));

            var ownId = Guid.NewGuid().ToString();
            var ownPath = DocumentRules.FolderDocumentPath(path);
            await _documentPlaces.AddAsync(new DocumentPlace(ownId, projectId, ownPath, ""));
            await _documents.AddAsync(new Document(ownId, projectId, id, ProjectPaths.NameOf(ownPath), DocumentRules.KindOf(ownPath), true, name, "", "",
                DocumentStatus.Draft, 0, 0, default, ""), "");

            var current = await FindAsync(parent.Id);
            await _folders.UpdateAsync(current with { ChildIds = [.. current.ChildIds.Where(child => child != id), id] }, current.ETag);
            return (IFolder)await FindAsync(id);
        });
    }

    /// <summary>Sets the pinned view and changes the settings of the views named in the layout. A view not named keeps
    /// its settings; a JSON null removes a view's settings.</summary>
    public async Task<IFolder> UpdateFolderLayoutAsync(string folderId, FolderLayout layout, string expectedETag)
    {
        var folder = await FindAsync(folderId);
        if (layout.PinnedView is not null) ViewNames.Check(layout.PinnedView);
        var projectFolders = (await _folders.GetFoldersByProjectIdAsync(folder.ProjectId)).Select(other => other.Id).ToHashSet(StringComparer.Ordinal);
        _views.CheckFolders(projectFolders.Contains, layout.Views);
        var views = new Dictionary<string, JsonElement>(folder.Views, StringComparer.Ordinal);
        foreach (var (name, settings) in layout.Views ?? new Dictionary<string, JsonElement>())
        {
            ViewNames.Check(name);
            if (ViewNames.Removes(settings)) views.Remove(name);
            else views[name] = settings.Clone();
        }
        ViewNames.CheckSettings(views);
        return await _folders.UpdateAsync(folder with { PinnedView = layout.PinnedView, Views = views }, expectedETag);
    }

    /// <summary>Changes the folder's place to the target folder, then puts it at <paramref name="index"/> among the
    /// target folder's children.</summary>
    public async Task<FolderMoveResult> MoveFolderToFolderAsync(string folderId, string targetFolderId, int index, string expectedETag)
    {
        var folder = await FindAsync(folderId);
        if (folder.IsRoot) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be moved.");
        var target = await FindAsync(targetFolderId);
        if (target.ProjectId != folder.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "A folder can only move inside its own project.");
        for (Folder? current = target; current is not null; current = current.ParentFolderId is { } parentId ? await _folders.GetByIdAsync(parentId) : null)
            if (current.Id == folder.Id) throw new WorkspaceException(WorkspaceError.Invalid, "A folder cannot move into itself.");
        return await _projectLock.RunLockedAsync(folder.ProjectId, async () =>
        {
            folder = await FindAsync(folderId);
            ETags.Check(folder.ETag, expectedETag);
            var oldParentId = folder.ParentFolderId!;
            if (oldParentId != targetFolderId)
            {
                var place = await _folderPlaces.GetPlaceByFolderIdAsync(folderId);
                var targetPath = await _folderPlaces.GetPathByFolderIdAsync(targetFolderId);
                await _folderPlaces.UpdateAsync(place with { Path = ProjectPaths.Join(targetPath, folder.Name) }, place.ETag);
            }
            target = await FindAsync(targetFolderId);
            var order = target.ChildIds.Where(child => child != folderId).ToList();
            order.Insert(Math.Clamp(index, 0, order.Count), folderId);
            await _folders.UpdateAsync(target with { ChildIds = order }, target.ETag);
            return new FolderMoveResult(await FindAsync(folderId), await FindAsync(oldParentId), await FindAsync(targetFolderId));
        });
    }

    /// <summary>Deletes the folder's own document and its place, then the folder and its place. View settings in other
    /// folders that name the deleted folder are cleared.</summary>
    public async Task DeleteFolderAsync(string folderId, string expectedETag)
    {
        var folder = await FindAsync(folderId);
        if (folder.IsRoot) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be deleted.");
        if (folder.ParentFolderId == folder.ProjectId && DefaultFolders.IsDefaultFolder(folder.Name)
            && !(_settings?.GetSettings().AllowDeletingDefaultFolders ?? false))
            throw new WorkspaceException(WorkspaceError.Forbidden, "Default project folders stay unless the server setting 'Allow deleting default project folders' is on.");
        await _projectLock.RunLockedAsync(folder.ProjectId, async () =>
        {
            folder = await FindAsync(folderId);
            ETags.Check(folder.ETag, expectedETag);
            if (folder.ChildIds.Count > 0)
                throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be deleted. Move their files and subfolders first.");
            if (folder.OwnDocumentId is { } ownId)
            {
                if (await _documents.GetByIdAsync(ownId) is { } own) await _documents.DeleteAsync(ownId, own.ETag);
                if (await _documentPlaces.GetByIdAsync(ownId) is { } ownPlace) await _documentPlaces.DeleteAsync(ownId, ownPlace.ETag);
            }
            await _folders.DeleteAsync(folderId, (await FindAsync(folderId)).ETag);
            await _folderPlaces.DeleteAsync(folderId, (await _folderPlaces.GetPlaceByFolderIdAsync(folderId)).ETag);
            foreach (var other in await _folders.GetFoldersByProjectIdAsync(folder.ProjectId))
            {
                var cleared = _views.WithoutFolder(other.Views, folderId);
                if (cleared.Count == 0) continue;
                var views = new Dictionary<string, JsonElement>(other.Views, StringComparer.Ordinal);
                foreach (var (name, settings) in cleared)
                {
                    if (ViewNames.Removes(settings)) views.Remove(name);
                    else views[name] = settings;
                }
                await _folders.UpdateAsync(other with { Views = views }, other.ETag);
            }
        });
    }

    private async Task<Folder> FindAsync(string folderId) => await _folders.GetByIdAsync(folderId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
}
