using Odysseum.Server.API.Models;
using Odysseum.Server.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/settings")]
public class ServerSettingsController(ISettingsProvider settingsProvider) : ControllerBase
{
    /// <summary>Server settings that apply to every project.</summary>
    [HttpGet]
    public IResult GetServerSettings() => ApiResults.Success(new ServerSettingsDto(settingsProvider.GetSettings().AllowDeletingDefaultFolders));

    /// <summary>Save server settings. Changes apply immediately, but an ODYSSEUM_* environment variable overrides them again after a restart.</summary>
    [HttpPut]
    public IResult UpdateServerSettings([FromBody] UpdateServerSettingsRequest request)
    {
        var settings = settingsProvider.GetSettings(copy: true);
        settings.AllowDeletingDefaultFolders = request.AllowDeletingDefaultFolders!.Value;
        settingsProvider.SaveSettings(settings);
        return ApiResults.Success(new ServerSettingsDto(settings.AllowDeletingDefaultFolders));
    }
}
