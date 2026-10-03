using Microsoft.AspNetCore.Mvc;
using Odysseum.Server.API.Models;
using Odysseum.Server.Plugins;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/plugins")]
public class PluginsController(IPluginRepository plugins) : ControllerBase
{
    /// <summary>Every installed plugin, also the disabled ones, with the URL of its client module.</summary>
    [HttpGet]
    public IResult GetPlugins() => ApiResults.SuccessCollection(plugins.GetAll().Select(PluginDto.FromPlugin));
}
