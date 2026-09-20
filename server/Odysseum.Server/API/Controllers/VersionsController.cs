using Odysseum.Server.API.Models;
using Odysseum.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/versions")]
public class VersionsController : ControllerBase
{
    private readonly ProjectLibrary _library;

    public VersionsController(ProjectLibrary library)
    {
        _library = library;
    }

    /// <summary>The project's saved versions, newest first.</summary>
    [HttpGet]
    public async Task<IResult> List(string project)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.SuccessCollection(await services.GetVersionsAsync());
    }

    /// <summary>Save the project as it is now under a name.</summary>
    [HttpPost]
    public async Task<IResult> Save(string project, [FromBody] SaveVersionRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        var version = await services.SaveVersionAsync(request.Name);
        return ApiResults.Created(version, $"/api/projects/{Uri.EscapeDataString(project)}/versions/{version!.Id}");
    }

    /// <summary>Put every file back as it was in that version. The state being replaced is saved first, so a restore can be undone.</summary>
    [HttpPost("{version}/restore")]
    public async Task<IResult> Restore(string project, string version)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.RestoreVersionAsync(version));
    }
}
