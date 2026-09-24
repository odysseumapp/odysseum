using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Files;

namespace Odysseum.Server.API.Views;

public static class FolderPaths
{
    public static string Normalize(string? path) => (path ?? "").Replace('\\', '/').Trim('/');

    public static (string Parent, string Name) Split(string path)
    {
        path = Normalize(path);
        if (path != "" && !FileManager.IsSafePath(path)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        return (Project.ParentPath(path), System.IO.Path.GetFileName(path));
    }

    public static Folder Find(IProject project, string? path)
    {
        var normalized = Normalize(path);
        if (normalized != "" && !FileManager.IsSafePath(normalized)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        return ProjectViews.Model(project).FolderAt(normalized)
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
    }

    public static async Task<IFolder> EnsureAsync(IFolderService folders, IProject project, string? path)
    {
        var normalized = Normalize(path);
        if (normalized != "" && !FileManager.IsSafePath(normalized)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        IFolder current = ProjectViews.Model(project).RootFolder;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Folders.FirstOrDefault(folder => folder.Name == segment) ?? await folders.CreateAsync(current, segment);
        }
        return current;
    }
}
