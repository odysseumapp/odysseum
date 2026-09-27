using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.Repositories;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/themes")]
public class ThemesController(IThemeRepository themes) : ControllerBase
{
    /// <summary>Every saved colour scheme. Each browser remembers which one it shows.</summary>
    [HttpGet]
    public IResult GetThemes() => ApiResults.SuccessCollection(themes.List().Select(theme => new ThemeDto(theme.Name, theme.Colors)));

    /// <summary>The roles a theme colours and the palettes they may name.</summary>
    [HttpGet("options")]
    public IResult GetThemeOptions() => ApiResults.Success(new ThemeOptionsDto(ThemeRepository.Roles, ThemeRepository.Palettes));

    /// <summary>One saved colour scheme.</summary>
    [HttpGet("{name}")]
    public IResult GetThemeByName(string name)
    {
        var theme = themes.Get(name);
        return ApiResults.Success(new ThemeDto(theme.Name, theme.Colors));
    }

    /// <summary>Save a colour scheme under this name, replacing one already saved with it.</summary>
    [HttpPut("{name}")]
    public IResult SaveTheme(string name, [FromBody] SaveThemeRequest request)
    {
        var theme = themes.Save(name, request.Colors);
        return ApiResults.Success(new ThemeDto(theme.Name, theme.Colors));
    }

    /// <summary>Delete a saved colour scheme.</summary>
    [HttpDelete("{name}")]
    public IResult DeleteTheme(string name)
    {
        themes.Delete(name);
        return ApiResults.Success(new DeletedItemDto(name));
    }
}
