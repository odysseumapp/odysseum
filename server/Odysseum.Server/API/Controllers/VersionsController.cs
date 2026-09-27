using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/versions")]
public class VersionsController(IProjectService projects, IHistoryService history, ProjectViews views) : ControllerBase
{
    /// <summary>The project's saved versions, newest first.</summary>
    [HttpGet]
    public async Task<IResult> List(string project)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.SuccessCollection((await history.ListVersionsAsync(current.Branch)).Select(Describe));
    }

    /// <summary>Save the project as it is now under a name.</summary>
    [HttpPost]
    public async Task<IResult> Save(string project, [FromBody] SaveVersionRequest request)
    {
        var current = await projects.GetAsync(project);
        var version = Describe(await history.SaveVersionAsync(current.Branch, request.Name));
        return ApiResults.Created(version, $"/api/projects/{Uri.EscapeDataString(project)}/versions/{version.Id}");
    }

    /// <summary>Put every file back as it was in that version. The server saves the current state first, so a restore can be undone.</summary>
    [HttpPost("{version}/restore")]
    public async Task<IResult> Restore(string project, string version)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.Success(views.View(ProjectViews.Model(await history.RestoreAsync(current.Branch, version))));
    }

    internal static VersionInfo Describe(ProjectVersion version) =>
        new(version.Id, version.Label, version.Automatic, version.Saved.UtcDateTime, version.Changes);
}
