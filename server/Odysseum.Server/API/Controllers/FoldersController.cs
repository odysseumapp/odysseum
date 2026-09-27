using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Folders;
using Odysseum.Server.API.Filters;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class FoldersController(IFolderService folders) : ControllerBase
{
    [HttpGet("projects/{projectId}/folders")]
    public async Task<IResult> GetFoldersByProjectId(string projectId) =>
        ApiResults.SuccessCollection((await folders.GetFoldersByProjectIdAsync(projectId)).Select(FolderDto.FromFolder));

    [HttpGet("folders/{folderId}")]
    public async Task<IResult> GetFolderById(string folderId)
    {
        var folder = await folders.GetFolderByIdAsync(folderId);
        return ApiResults.Success(FolderDto.FromFolder(folder), folder.ETag);
    }

    /// <summary>Make an empty folder at the end of the parent folder's children.</summary>
    [HttpPost("folders")]
    public async Task<IResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        var folder = await folders.CreateFolderAsync(request.ParentFolderId, request.Name);
        return ApiResults.Created(FolderDto.FromFolder(folder), $"/api/folders/{folder.Id}", folder.ETag);
    }

    /// <summary>Set the folder's pinned view and change the settings of some of its views.</summary>
    [HttpPut("folders/{folderId}/layout")]
    [RequireIfMatch]
    public async Task<IResult> UpdateFolderLayout(string folderId, [FromBody] UpdateFolderLayoutRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var layout = new FolderLayout { PinnedView = request.PinnedView, Views = request.Views };
        var folder = await folders.UpdateFolderLayoutAsync(folderId, layout, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(FolderDto.FromFolder(folder), folder.ETag);
    }

    /// <summary>Move the folder into another folder, or to another place in the same folder.</summary>
    [HttpPut("folders/{folderId}/parent")]
    [RequireIfMatch]
    public async Task<IResult> MoveFolderToFolder(string folderId, [FromBody] MoveFolderRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var result = await folders.MoveFolderToFolderAsync(folderId, request.TargetFolderId, request.Index!.Value, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(FolderMoveDto.FromMoveResult(result), result.Folder.ETag);
    }

    /// <summary>Delete an empty folder. Files, hidden files and subfolders prevent the delete.</summary>
    [HttpDelete("folders/{folderId}")]
    [RequireIfMatch]
    public async Task<IResult> DeleteFolder(string folderId, [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        await folders.DeleteFolderAsync(folderId, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(new DeletedItemDto(folderId));
    }
}
