using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/folders")]
public class FoldersController(ProjectLibrary library) : ControllerBase
{
    /// <summary>Create an empty folder. All folders support the same views.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateFolderRequest request) =>
        ApiResults.Success(await (await library.OpenServicesAsync(project)).CreateFolderAsync(request));

    /// <summary>Remove an empty folder. Files, hidden files, and subfolders prevent removal.</summary>
    [HttpDelete]
    public async Task<IResult> Remove(string project, [FromBody] RemoveFolderRequest request) =>
        ApiResults.Success(await (await library.OpenServicesAsync(project)).RemoveFolderAsync(request));

    /// <summary>Save a folder's pinned view, child order, and which folder supplies its grid's columns.</summary>
    [HttpPut("layout")]
    public async Task<IResult> Layout(string project, [FromBody] FolderLayoutRequest request) =>
        ApiResults.Success(await (await library.OpenServicesAsync(project)).SaveFolderLayoutAsync(request));
}
