using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Themes;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/themes")]
public class ThemesController(ThemeStore themes) : ControllerBase
{
    /// <summary>Every saved colour scheme. Which one a browser is showing is remembered by that browser.</summary>
    [HttpGet]
    public IResult List() => ApiResults.SuccessCollection(themes.List().Select(Describe));

    /// <summary>The roles a theme colours and the palettes they may name.</summary>
    [HttpGet("options")]
    public IResult Options() => ApiResults.Success(new ThemeOptionsResponse(ThemeStore.Roles, ThemeStore.Palettes));

    /// <summary>One saved colour scheme.</summary>
    [HttpGet("{name}")]
    public IResult Get(string name) => ApiResults.Success(Describe(themes.Get(name)));

    /// <summary>Save a colour scheme under this name, replacing one already saved with it.</summary>
    [HttpPut("{name}")]
    public IResult Save(string name, [FromBody] ThemeRequest request) => ApiResults.Success(Describe(themes.Save(name, request.Colors)));

    /// <summary>Forget a saved colour scheme.</summary>
    [HttpDelete("{name}")]
    public IResult Delete(string name)
    {
        themes.Delete(name);
        return ApiResults.Success(new { deleted = name });
    }

    private static ThemeResponse Describe(Theme theme) => new(theme.Name, theme.Colors);
}
