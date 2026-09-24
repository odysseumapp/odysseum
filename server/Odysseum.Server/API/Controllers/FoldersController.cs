using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Services;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/folders")]
public class FoldersController(IProjectService projects, IFolderService folders, IOrderService order, ProjectViews views) : ControllerBase
{
    /// <summary>Create an empty folder. All folders support the same views.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateFolderRequest request)
    {
        var current = await projects.GetAsync(project);
        if (request.Revision != current.Revision) throw new WorkspaceException(WorkspaceError.Conflict, "The project changed. Refresh before saving again.");
        var (parentPath, name) = FolderPaths.Split(request.Path);
        var parent = ProjectViews.Model(current).FolderAt(parentPath)
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "The parent folder no longer exists.");
        var created = await folders.CreateAsync(parent, name);
        return ApiResults.Success(await views.ProjectAsync(created.Project));
    }

    /// <summary>Remove an empty folder. Files, hidden files, and subfolders prevent removal.</summary>
    [HttpDelete]
    public async Task<IResult> Remove(string project, [FromBody] RemoveFolderRequest request)
    {
        var current = await projects.GetAsync(project);
        await folders.RemoveAsync(FolderPaths.Find(current, request.Path), request.Revision);
        return ApiResults.Success(await views.ProjectAsync(await projects.GetAsync(project)));
    }

    /// <summary>Save a folder's pinned view, child order, and which folder supplies its grid's columns.</summary>
    [HttpPut("layout")]
    public async Task<IResult> Layout(string project, [FromBody] FolderLayoutRequest request)
    {
        if (request.Path is null || request.ItemOrder is null || request.PinnedView is not (null or "write" or "board" or "outline" or "grid"))
            throw new WorkspaceException(WorkspaceError.Invalid, "Invalid folder layout.");
        var current = await projects.GetAsync(project);
        var folder = FolderPaths.Find(current, request.Path);
        var children = await order.ChildrenAsync(folder);
        var keys = children.ToDictionary(child => child.Folder is { } sub ? "folder:" + sub.Name : child.Document!.Id,
            child => child.Folder?.Id ?? child.Document!.Id, StringComparer.Ordinal);
        if (request.ItemOrder.Distinct().Count() != request.ItemOrder.Length || request.ItemOrder.Any(key => !keys.ContainsKey(key)))
            throw new WorkspaceException(WorkspaceError.Invalid, "Layouts must refer to immediate children.");
        var layout = new FolderLayout
        {
            PinnedView = request.PinnedView is null ? null : Enum.Parse<FolderView>(request.PinnedView, ignoreCase: true),
            GridFolderId = request.GridFolder,
        };
        var updated = await folders.SetLayoutAsync(folder, layout, request.Revision);
        await order.ArrangeChildrenAsync(updated, request.ItemOrder.Select(key => keys[key]).ToArray(), updated.Project.Revision);
        return ApiResults.Success(await views.ProjectAsync(await projects.GetAsync(project)));
    }
}
