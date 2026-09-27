using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Folders.Events;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Models.Editing;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class FolderService : IFolderService
{
    private readonly ProjectSessions _sessions;
    private readonly ISettingsProvider? _settings;
    private readonly ViewCatalog _views;

    public FolderService(ProjectSessions sessions, ISettingsProvider? settings = null, ViewCatalog? views = null)
    {
        _sessions = sessions;
        _settings = settings;
        _views = views ?? ViewCatalog.Default;
        sessions.FoldersRemoved += (session, folders) => { foreach (var folder in folders) FolderRemoved?.Invoke(this, new(session.Branch, folder)); };
    }

    public event EventHandler<FolderEventArgs>? FolderCreated;
    public event EventHandler<FolderEventArgs>? FolderUpdated;
    public event EventHandler<FolderEventArgs>? FolderRemoved;

    public Task<IReadOnlyList<IFolder>> ListAsync(IProject project) => Task.FromResult<IReadOnlyList<IFolder>>(Model(project).Folders);

    public Task<IFolder> GetAsync(IProject project, string id) => Task.FromResult<IFolder>(Find(Model(project), id));

    public async Task<IFolder> CreateAsync(ProjectBranch branch, string parentId, string name)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IFolder>(async () =>
        {
            var current = session.Current;
            var editor = new FolderEditor(current);
            var created = editor.Create(parentId, name);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var folder = Find(saved, created.Id);
            FolderCreated?.Invoke(this, new(branch, folder));
            return folder;
        });
    }

    public async Task<IFolder> SetLayoutAsync(ProjectBranch branch, string folderId, FolderLayout layout, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IFolder>(async () =>
        {
            var current = session.Current;
            CheckRevision(current, expectedRevision);
            _views.CheckFolders(current, layout.Views);
            var editor = new FolderEditor(current);
            editor.SetLayout(folderId, layout);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var folder = Find(saved, folderId);
            FolderUpdated?.Invoke(this, new(branch, folder));
            return folder;
        });
    }

    public async Task<IFolder> MoveAsync(ProjectBranch branch, string itemId, string targetFolderId, int index, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IFolder>(async () =>
        {
            var current = session.Current;
            CheckRevision(current, expectedRevision);
            var editor = new FolderEditor(current);
            editor.Move(itemId, targetFolderId, index);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var target = Find(saved, targetFolderId);
            FolderUpdated?.Invoke(this, new(branch, target));
            return target;
        });
    }

    public async Task RemoveAsync(ProjectBranch branch, string folderId, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        await session.RunAsync(async () =>
        {
            var current = session.Current;
            CheckRevision(current, expectedRevision);
            var folder = Find(current, folderId);
            if (DefaultFolders.IsDefaultFolder(folder.Path) && !(_settings?.GetSettings().AllowDeletingDefaultFolders ?? false))
                throw new WorkspaceException(WorkspaceError.Forbidden, "Default project folders stay unless the server setting 'Allow deleting default project folders' is on.");
            var editor = new FolderEditor(current);
            editor.Remove(folderId);
            // View settings that name the removed folder are cleared.
            foreach (var other in current.Folders.Where(other => other.Id != folderId))
            {
                var cleared = _views.WithoutFolder(other.Views, folderId);
                if (cleared.Count > 0) editor.SetLayout(other.Id, new FolderLayout { PinnedView = other.PinnedView, Views = cleared });
            }
            await session.SaveAsync(editor.Changes(), current.Revision);
            FolderRemoved?.Invoke(this, new(branch, folder));
            return true;
        });
    }

    private static void CheckRevision(Project project, string expectedRevision)
    {
        if (expectedRevision != project.Revision)
            throw new WorkspaceException(WorkspaceError.Conflict, "The project changed. Refresh before saving again.");
    }

    private static Folder Find(Project project, string id) => project.Folder(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");

    private static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
