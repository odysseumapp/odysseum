using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Storage;

namespace Odysseum.Server.Services.Projects;

/// <summary>Folder operations and view layouts, independent of document kinds.</summary>
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
        if (request.Path is null || request.ItemOrder is null || request.Rows is null || request.Columns is null
            || request.PinnedView is not (null or "write" or "board" or "outline" or "grid")
            || request.Axis is not (null or "rows" or "columns"))
            throw new WorkspaceException(400, "Invalid folder layout.");
        var candidate = state.Manifest.Clone();
        var folder = request.Path == "" ? candidate : candidate.FolderManifests.GetValueOrDefault(request.Path)
            ?? throw new WorkspaceException(404, "The folder no longer exists.");
        var keys = state.Documents.Values.Where(d => (Path.GetDirectoryName(d.Path)?.Replace('\\', '/') ?? "") == request.Path)
            .Select(d => d.Id).Concat(folder.Folders.Values.Select(f => "folder:" + f.Path)).ToHashSet(StringComparer.Ordinal);
        if (request.ItemOrder.Distinct().Count() != request.ItemOrder.Length || request.ItemOrder.Any(key => !keys.Contains(key)))
            throw new WorkspaceException(400, "Layouts must refer to immediate children.");
        if (request.Rows.Length > 200 || request.Rows.Distinct().Count() != request.Rows.Length
            || request.Rows.Any(id => id is null || !state.Documents.ContainsKey(id)))
            throw new WorkspaceException(400, "One of the grid's rows no longer exists.");
        var folderIds = candidate.FolderManifests.Values.Select(f => f.Id).Append(candidate.Id).ToHashSet(StringComparer.Ordinal);
        if (request.Columns.Length > 200 || request.Columns.Distinct().Count() != request.Columns.Length
            || request.Columns.Any(key => key is null || !(key.EndsWith("/*") ? folderIds.Contains(key[..^2]) : folderIds.Contains(key) || state.Documents.ContainsKey(key))))
            throw new WorkspaceException(400, "One of the grid's columns no longer exists.");
        folder.PinnedView = request.PinnedView;
        folder.ItemOrder = [.. request.ItemOrder];
        folder.Rows = [.. request.Rows];
        folder.Columns = [.. request.Columns];
        folder.Axis = request.Axis;
        await state.CommitManifestAsync(candidate, request.Revision);
        state.PublishChanges();
    }
}
