using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.Repositories;

public sealed class FolderRepository(OpenProject project) : IRepository<Folder, string>
{
    public Task<Folder?> GetAsync(string id) => Task.FromResult(project.Current.Folder(id));

    public Task<IReadOnlyList<Folder>> GetAllAsync() => Task.FromResult(project.Current.Folders);

    public Task SaveAsync(Folder folder) => SaveAsync([folder]);

    public async Task SaveAsync(IReadOnlyList<Folder> folders)
    {
        var candidate = project.Current.Manifest.Clone();
        foreach (var folder in folders)
        {
            var current = Find(folder.Id);
            var manifest = current.IsRoot ? candidate : candidate.FolderManifests.GetValueOrDefault(current.Path)
                ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
            manifest.PinnedView = folder.PinnedView?.ToString().ToLowerInvariant();
            manifest.GridFolder = folder.GridFolderId;
            manifest.ItemOrder = [.. folder.ItemOrder];
        }
        await project.CommitAsync(candidate, project.Current.Revision);
    }

    public async Task DeleteAsync(string id)
    {
        var folder = Find(id);
        if (folder.IsRoot) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be removed.");
        project.Files.RemoveEmptyFolder(folder.Path);
        await project.RescanAsync();
    }

    public async Task<Folder> CreateAsync(Folder parent, string name)
    {
        var path = Find(parent.Id).ChildPath(name);
        project.Files.CreateFolder(path);
        await project.RescanAsync();
        return project.Current.FolderAt(path) ?? throw new WorkspaceException(WorkspaceError.Unavailable, "The folder was created but could not be read back.");
    }

    private Folder Find(string id) => project.Current.Folder(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
}
