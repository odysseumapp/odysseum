using Microsoft.Extensions.FileProviders;
using Odysseum.Server.Plugins;

namespace Odysseum.Server.Bootstrap;

public static class PluginHosting
{
    /// <summary>Serves each enabled plugin's <c>wwwroot</c> folder at <c>/plugins/{id}/</c>.</summary>
    public static void UsePluginFiles(this WebApplication app)
    {
        foreach (var plugin in app.Services.GetRequiredService<IPluginRepository>().GetAll())
        {
            if (!plugin.Enabled || !Directory.Exists(plugin.WwwRoot)) continue;
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/plugins/" + plugin.Id,
                FileProvider = new PhysicalFileProvider(plugin.WwwRoot),
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
            });
        }
    }
}
