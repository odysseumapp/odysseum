using System.Text;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services;
using Odysseum.Server.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectLibrary _library;

    public ProjectsController(ProjectLibrary library)
    {
        _library = library;
    }

    /// <summary>List the projects in the workspace.</summary>
    [HttpGet]
    public async Task<IResult> List()
    {
        return ApiResults.SuccessCollection(await _library.ListAsync());
    }

    /// <summary>Create a project folder with its own metadata.</summary>
    [HttpPost]
    public async Task<IResult> Create([FromBody] CreateProjectRequest request)
    {
        var created = await _library.CreateAsync(request);
        return ApiResults.Created(created, $"/api/projects/{Uri.EscapeDataString(created.Slug)}");
    }

    /// <summary>The project's settings, documents, and folder layouts.</summary>
    [HttpGet("{project}")]
    public async Task<IResult> Get(string project)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.GetProjectAsync());
    }

    /// <summary>The project's settings: title and word goals.</summary>
    [HttpGet("{project}/settings")]
    public async Task<IResult> GetSettings(string project)
    {
        var handle = await _library.OpenAsync(project);
        return ApiResults.Success(await handle.Settings.GetSettingsAsync());
    }

    /// <summary>Update the project settings and return the project with its new revision.</summary>
    [HttpPut("{project}/settings")]
    public async Task<IResult> UpdateSettings(string project, [FromBody] ProjectSettingsRequest request)
    {
        var handle = await _library.OpenAsync(project);
        var settings = new ProjectSettings { Title = request.Title, WordGoal = request.WordGoal, DefaultSceneWordGoal = request.DefaultSceneWordGoal };
        return ApiResults.Success(await handle.Settings.SaveSettingsAsync(settings, request.Revision));
    }

    /// <summary>Set the manuscript order of every document.</summary>
    [HttpPut("{project}/order")]
    public async Task<IResult> Reorder(string project, [FromBody] ReorderRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.ReorderAsync(request));
    }

    /// <summary>Download the saved manuscript as one Markdown file.</summary>
    [HttpGet("{project}/export")]
    public async Task<IResult> Export(string project)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.File(Encoding.UTF8.GetBytes(await services.ExportAsync()), "text/markdown; charset=utf-8", "manuscript.md");
    }

    /// <summary>Search prose, titles, synopses, and notes.</summary>
    [HttpGet("{project}/search")]
    public async Task<IResult> Search(string project, [FromQuery] string q = "")
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.SuccessCollection(await services.SearchAsync(q));
    }
}
