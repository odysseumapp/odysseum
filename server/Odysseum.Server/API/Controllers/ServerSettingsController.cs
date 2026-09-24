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
    public IResult Get() => ApiResults.Success(Describe(settingsProvider.GetSettings()));

    /// <summary>Save server settings. Changes apply immediately, but an ODYSSEUM_* environment variable overrides them again after a restart.</summary>
    [HttpPut]
    public IResult Update([FromBody] ServerSettingsRequest request)
    {
        var settings = settingsProvider.GetSettings(copy: true);
        settings.AllowDeletingDefaultFolders = request.AllowDeletingDefaultFolders;
        settingsProvider.SaveSettings(settings);
        return ApiResults.Success(Describe(settings));
    }

    private static ServerSettingsResponse Describe(IServerSettings settings) => new(settings.AllowDeletingDefaultFolders);
}
