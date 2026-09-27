using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/folders")]
public class FoldersController(IProjectService projects, IFolderService folders, ProjectViews views) : ControllerBase
{
    /// <summary>Create an empty folder. All folders support the same views.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateFolderRequest request)
    {
        var current = ProjectViews.Model(await projects.GetAsync(project));
        if (request.Revision != current.Revision) throw new WorkspaceException(WorkspaceError.Conflict, "The project changed. Refresh before saving again.");
        var (parentPath, name) = FolderPaths.Split(request.Path);
        var parent = current.FolderAt(parentPath) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The parent folder no longer exists.");
        await folders.CreateAsync(current.Branch, parent.Id, name);
        return ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(current.Branch))));
    }

    /// <summary>Remove an empty folder. Files, hidden files, and subfolders prevent removal.</summary>
    [HttpDelete]
    public async Task<IResult> Remove(string project, [FromBody] RemoveFolderRequest request)
    {
        var current = await projects.GetAsync(project);
        await folders.RemoveAsync(current.Branch, FolderPaths.Find(current, request.Path).Id, request.Revision);
        return ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(current.Branch))));
    }

    /// <summary>Save a folder's pinned view and which folder supplies its grid's columns.</summary>
    [HttpPut("layout")]
    public async Task<IResult> Layout(string project, [FromBody] FolderLayoutRequest request)
    {
        if (request.Path is null || request.PinnedView is not (null or "write" or "board" or "outline" or "grid"))
            throw new WorkspaceException(WorkspaceError.Invalid, "Invalid folder layout.");
        var current = await projects.GetAsync(project);
        var folder = FolderPaths.Find(current, request.Path);
        var layout = new FolderLayout
        {
            PinnedView = request.PinnedView is null ? null : Enum.Parse<FolderView>(request.PinnedView, ignoreCase: true),
            GridFolderId = request.GridFolder,
        };
        await folders.SetLayoutAsync(current.Branch, folder.Id, layout, request.Revision);
        return ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(current.Branch))));
    }

    /// <summary>Put a folder or document at an index among a folder's children. This moves it between folders and changes the order.</summary>
    [HttpPut("move")]
    public async Task<IResult> Move(string project, [FromBody] MoveItemRequest request)
    {
        if (string.IsNullOrEmpty(request.Id) || string.IsNullOrEmpty(request.TargetFolder) || request.Index < 0)
            throw new WorkspaceException(WorkspaceError.Invalid, "A move names the item, the target folder and an index.");
        var current = await projects.GetAsync(project);
        await folders.MoveAsync(current.Branch, request.Id, request.TargetFolder, request.Index, request.Revision);
        return ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(current.Branch))));
    }
}
