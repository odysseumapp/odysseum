using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Templates;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController(ITemplateRepository templates, ProjectTemplateService templateService) : ControllerBase
{
    /// <summary>Every project template a new project can start from. <c>Default</c> is always among them.</summary>
    [HttpGet]
    public IResult GetTemplates() => ApiResults.SuccessCollection(templates.List().Select(TemplateDto.FromTemplate));

    [HttpGet("{name}")]
    public IResult GetTemplateByName(string name) => ApiResults.Success(TemplateDto.FromTemplate(templates.Get(name)));

    /// <summary>Save a project as a template under this name. A template with the same name is replaced.</summary>
    [HttpPut("{name}")]
    public async Task<IResult> SaveTemplate(string name, [FromBody] SaveTemplateRequest request)
    {
        var template = await templateService.CaptureTemplateAsync(request.ProjectId, TemplateRepository.ValidName(name));
        return ApiResults.Success(TemplateDto.FromTemplate(templates.Save(template)));
    }

    /// <summary>Delete a project template. Deleting <c>Default</c> puts back the one Odysseum ships with.</summary>
    [HttpDelete("{name}")]
    public IResult DeleteTemplate(string name)
    {
        templates.Delete(name);
        return ApiResults.Success(new DeletedItemDto(name));
    }
}
