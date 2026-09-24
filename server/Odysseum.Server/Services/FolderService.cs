using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Folders.Events;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class FolderService : IFolderService
{
    private readonly ISettingsProvider? _settings;

    public FolderService(ProjectLibrary library, ISettingsProvider? settings = null)
    {
        _settings = settings;
        library.FolderRemoved += folder => FolderRemoved?.Invoke(this, new(folder));
    }

    public event EventHandler<FolderEventArgs>? FolderCreated;
    public event EventHandler<FolderEventArgs>? FolderUpdated;
    public event EventHandler<FolderEventArgs>? FolderRemoved;

    public Task<IReadOnlyList<IFolder>> ListAsync(IProject project) => Task.FromResult<IReadOnlyList<IFolder>>(Model(project).Folders);

    public Task<IFolder> GetAsync(IProject project, string id) => Task.FromResult<IFolder>(Find(Model(project), id));

    public Task<IFolder> CreateAsync(IFolder parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || !FileManager.IsSafePath(name))
            throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        var open = OpenProject.Of(parent);
        return open.RunAsync<IFolder>(async () =>
        {
            var created = await open.Folders.CreateAsync(Find(open.Current, parent.Id), name);
            FolderCreated?.Invoke(this, new(created));
            return created;
        });
    }

    public Task<IFolder> SetLayoutAsync(IFolder folder, FolderLayout layout, string expectedRevision)
    {
        var open = OpenProject.Of(folder);
        return open.RunAsync<IFolder>(async () =>
        {
            CheckRevision(open.Current, expectedRevision);
            var current = Find(open.Current, folder.Id);
            if (layout.PinnedView is { } view && !Enum.IsDefined(view)) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid folder layout.");
            if (layout.GridFolderId is not null && open.Current.Folder(layout.GridFolderId) is null)
                throw new WorkspaceException(WorkspaceError.Invalid, "The grid's column folder no longer exists.");
            await open.Folders.SaveAsync(current.With(layout));
            var saved = Find(open.Current, folder.Id);
            FolderUpdated?.Invoke(this, new(saved));
            return saved;
        });
    }

    public Task RemoveAsync(IFolder folder, string expectedRevision)
    {
        var open = OpenProject.Of(folder);
        return open.RunAsync(async () =>
        {
            CheckRevision(open.Current, expectedRevision);
            var current = Find(open.Current, folder.Id);
            if (ProjectLibrary.IsDefaultFolder(current.Path) && !(_settings?.GetSettings().AllowDeletingDefaultFolders ?? false))
                throw new WorkspaceException(WorkspaceError.Forbidden, "Default project folders stay unless the server setting 'Allow deleting default project folders' is on.");
            await open.Folders.DeleteAsync(current.Id);
            FolderRemoved?.Invoke(this, new(current));
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
