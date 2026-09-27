using System.Text;
using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Filters;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class ProjectsController(IProjectService projects, IDocumentService documents, ManuscriptExportService export) : ControllerBase
{
    /// <summary>Every project in the workspace, by title.</summary>
    [HttpGet("projects")]
    public async Task<IResult> GetProjects() =>
        ApiResults.SuccessCollection((await projects.GetAllProjectsAsync()).Select(ProjectDto.FromProject));

    /// <summary>Make a project from a template.</summary>
    [HttpPost("projects")]
    public async Task<IResult> CreateProject([FromBody] CreateProjectRequest request)
    {
        var project = await projects.CreateProjectAsync(request.Title, request.WordGoal, request.TemplateName);
        return ApiResults.Created(ProjectDto.FromProject(project), $"/api/projects/{project.Id}", project.ETag);
    }

    [HttpGet("projects/{projectId}")]
    public async Task<IResult> GetProjectById(string projectId)
    {
        var project = await projects.GetProjectByIdAsync(projectId);
        return ApiResults.Success(ProjectDto.FromProject(project), project.ETag);
    }

    /// <summary>Replace the project's title and word goals.</summary>
    [HttpPut("projects/{projectId}/settings")]
    [RequireIfMatch]
    public async Task<IResult> UpdateProjectSettings(string projectId, [FromBody] UpdateProjectSettingsRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var settings = new ProjectSettings(request.Title, request.WordGoal!.Value, request.DefaultSceneWordGoal!.Value);
        var project = await projects.UpdateProjectSettingsAsync(projectId, settings, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(ProjectDto.FromProject(project), project.ETag);
    }

    /// <summary>Download the project's scenes as one Markdown file, in manuscript order.</summary>
    [HttpGet("projects/{projectId}/export")]
    public async Task<IResult> ExportManuscript(string projectId) =>
        ApiResults.File(Encoding.UTF8.GetBytes(await export.ExportManuscriptAsync(projectId)), "text/markdown; charset=utf-8", "manuscript.md");

    /// <summary>Find documents whose title, synopsis, notes or text contain the search text. At most 50 are returned.</summary>
    [HttpGet("projects/{projectId}/search")]
    public async Task<IResult> SearchDocuments(string projectId, [FromQuery] string text = "") =>
        ApiResults.SuccessCollection((await documents.SearchDocumentsAsync(projectId, text))
            .Select(result => new SearchResultDto(result.Document.Id, result.Document.Title, result.Excerpt)));
}
