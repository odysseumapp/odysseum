using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Templates;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController(ITemplateRepository templates, IProjectService projects, ProjectTemplateService capture) : ControllerBase
{
    /// <summary>Every project template a new project can start from. <c>Default</c> is always among them.</summary>
    [HttpGet]
    public IResult List() => ApiResults.SuccessCollection(templates.List().Select(Describe));

    /// <summary>One project template.</summary>
    [HttpGet("{name}")]
    public IResult Get(string name) => ApiResults.Success(Describe(templates.Get(name)));

    /// <summary>Save a project as a template under this name, replacing one already saved with it.</summary>
    [HttpPut("{name}")]
    public async Task<IResult> Save(string name, [FromBody] SaveTemplateRequest request)
    {
        var project = ProjectViews.Model(await projects.GetAsync(request.Project));
        return ApiResults.Success(Describe(templates.Save(capture.Capture(project, TemplateRepository.ValidName(name)))));
    }

    /// <summary>Forget a project template. Deleting <c>Default</c> restores the one Odysseum ships with.</summary>
    [HttpDelete("{name}")]
    public IResult Delete(string name)
    {
        templates.Delete(name);
        return ApiResults.Success(new { deleted = name });
    }

    private static TemplateResponse Describe(ProjectTemplate template) => new(template.Name,
        new(template.Settings.WordGoal, template.Settings.DefaultSceneWordGoal),
        [.. template.Folders.Select(folder => new TemplateFolderResponse(folder.Path, folder.PinnedView, folder.Children, folder.Views))],
        [.. template.Documents.Select(document => new TemplateDocumentResponse(document.Path, document.Title))]);
}
