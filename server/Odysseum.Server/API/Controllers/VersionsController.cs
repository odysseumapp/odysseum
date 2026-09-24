using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Services;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/versions")]
public class VersionsController(ProjectLibrary library, ProjectViews views) : ControllerBase
{
    /// <summary>The project's saved versions, newest first.</summary>
    [HttpGet]
    public async Task<IResult> List(string project) =>
        ApiResults.SuccessCollection(await (await library.OpenProjectAsync(project)).ListVersionsAsync());

    /// <summary>Save the project as it is now under a name.</summary>
    [HttpPost]
    public async Task<IResult> Save(string project, [FromBody] SaveVersionRequest request)
    {
        var version = await (await library.OpenProjectAsync(project)).SaveVersionAsync(request.Name);
        return ApiResults.Created(version, $"/api/projects/{Uri.EscapeDataString(project)}/versions/{version.Id}");
    }

    /// <summary>Put every file back as it was in that version. The server saves the current state first, so a restore can be undone.</summary>
    [HttpPost("{version}/restore")]
    public async Task<IResult> Restore(string project, string version) =>
        ApiResults.Success(await views.ProjectAsync(await (await library.OpenProjectAsync(project)).RestoreVersionAsync(version)));
}
