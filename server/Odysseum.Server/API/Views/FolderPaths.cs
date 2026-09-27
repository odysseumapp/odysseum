using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Files;

namespace Odysseum.Server.API.Views;

/// <summary>Turns the paths the API uses into folders.</summary>
public static class FolderPaths
{
    public static string Normalize(string? path) => (path ?? "").Replace('\\', '/').Trim('/');

    public static (string Parent, string Name) Split(string path)
    {
        path = Normalize(path);
        if (path != "" && !FileManager.IsSafePath(path)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        return (Item.ParentPath(path), System.IO.Path.GetFileName(path));
    }

    public static Folder Find(IProject project, string? path)
    {
        var normalized = Normalize(path);
        if (normalized != "" && !FileManager.IsSafePath(normalized)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        return ProjectViews.Model(project).FolderAt(normalized)
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
    }

    /// <summary>The folder at the path, creating the missing folders on the way.</summary>
    public static async Task<Folder> EnsureAsync(IFolderService folders, IProject project, string? path)
    {
        var normalized = Normalize(path);
        if (normalized != "" && !FileManager.IsSafePath(normalized)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        var current = ProjectViews.Model(project).Root;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Folders.FirstOrDefault(folder => folder.Name == segment)
                ?? (Folder)await folders.CreateAsync(project.Branch, current.Id, segment);
        }
        return current;
    }
}
