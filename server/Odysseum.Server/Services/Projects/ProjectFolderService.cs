using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Storage;

namespace Odysseum.Server.Services.Projects;

internal sealed class ProjectFolderService(ProjectState state, ProjectFileStore files, Func<bool> allowDeletingDefaultFolders)
{
    private void CheckRevision(string revision)
    {
        if (revision != state.Revision) throw new WorkspaceException(409, "The project changed. Refresh before saving again.");
    }

    public void Create(CreateFolderRequest request)
    {
        CheckRevision(request.Revision);
        files.CreateFolder(request.Path);
    }

    public void Remove(RemoveFolderRequest request)
    {
        CheckRevision(request.Revision);
        if (ProjectLibrary.IsDefaultFolder(request.Path) && !allowDeletingDefaultFolders())
            throw new WorkspaceException(403, "Default project folders stay unless the server setting 'Allow deleting default project folders' is on.");
        files.RemoveEmptyFolder(request.Path);
    }

    public async Task SaveLayoutAsync(FolderLayoutRequest request)
    {
        CheckRevision(request.Revision);
        if (request.Path is null || request.ItemOrder is null
            || request.PinnedView is not (null or "write" or "board" or "outline" or "grid"))
            throw new WorkspaceException(400, "Invalid folder layout.");
        var candidate = state.Manifest.Clone();
        var folder = request.Path == "" ? candidate : candidate.FolderManifests.GetValueOrDefault(request.Path)
            ?? throw new WorkspaceException(404, "The folder no longer exists.");
        var keys = state.Documents.Values.Where(d => (Path.GetDirectoryName(d.Path)?.Replace('\\', '/') ?? "") == request.Path)
            .Select(d => d.Id).Concat(folder.Folders.Values.Select(f => "folder:" + f.Path)).ToHashSet(StringComparer.Ordinal);
        if (request.ItemOrder.Distinct().Count() != request.ItemOrder.Length || request.ItemOrder.Any(key => !keys.Contains(key)))
            throw new WorkspaceException(400, "Layouts must refer to immediate children.");
        if (request.GridFolder is not null && request.GridFolder != candidate.Id && candidate.FolderManifests.Values.All(f => f.Id != request.GridFolder))
            throw new WorkspaceException(400, "The grid's column folder no longer exists.");
        folder.PinnedView = request.PinnedView;
        folder.ItemOrder = [.. request.ItemOrder];
        folder.GridFolder = request.GridFolder;
        await state.CommitManifestAsync(candidate, request.Revision);
        state.PublishChanges();
    }
}
