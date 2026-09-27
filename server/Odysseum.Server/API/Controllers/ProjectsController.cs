using System.Text;
using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Services.Projects;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(ProjectSessions sessions, IProjectService projects, ProjectViews views) : ControllerBase
{
    /// <summary>List the projects in the workspace.</summary>
    [HttpGet]
    public async Task<IResult> List() => ApiResults.SuccessCollection(await sessions.ListAsync());

    /// <summary>Create a project folder with its own metadata.</summary>
    [HttpPost]
    public async Task<IResult> Create([FromBody] CreateProjectRequest request)
    {
        var created = await sessions.CreateAsync(request.Title, request.WordGoal, request.Template);
        return ApiResults.Created(created, $"/api/projects/{Uri.EscapeDataString(created.Name)}");
    }

    /// <summary>The project's settings, documents, and folder layouts.</summary>
    [HttpGet("{project}")]
    public async Task<IResult> Get(string project) => ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(project))));

    /// <summary>The project's settings: title and word goals.</summary>
    [HttpGet("{project}/settings")]
    public async Task<IResult> GetSettings(string project)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.Success(new Settings.ProjectSettings { Title = current.Title, WordGoal = current.WordGoal, DefaultSceneWordGoal = current.DefaultSceneWordGoal });
    }

    /// <summary>Update the project settings and return the project with its new revision.</summary>
    [HttpPut("{project}/settings")]
    public async Task<IResult> UpdateSettings(string project, [FromBody] ProjectSettingsRequest request)
    {
        var current = await projects.GetAsync(project);
        var settings = new ProjectSettings { Title = request.Title, WordGoal = request.WordGoal, DefaultSceneWordGoal = request.DefaultSceneWordGoal };
        var updated = await projects.SaveSettingsAsync(current.Branch, settings, request.Revision);
        return ApiResults.Success(views.View(ProjectViews.Model(updated)));
    }

    /// <summary>Download the saved manuscript as one Markdown file.</summary>
    [HttpGet("{project}/export")]
    public async Task<IResult> Export(string project) =>
        ApiResults.File(Encoding.UTF8.GetBytes(await views.ExportAsync(ProjectViews.Model(await projects.GetAsync(project)))), "text/markdown; charset=utf-8", "manuscript.md");

    /// <summary>Search prose, titles, synopses, and notes.</summary>
    [HttpGet("{project}/search")]
    public async Task<IResult> Search(string project, [FromQuery] string q = "") =>
        ApiResults.SuccessCollection(await views.SearchAsync(ProjectViews.Model(await projects.GetAsync(project)), q));
}
