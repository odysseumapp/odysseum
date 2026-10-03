using System.Text.Json.Serialization;
using Odysseum.Server.Plugins;

namespace Odysseum.Server.API.Models;

/// <summary>An installed plugin. <c>Status</c> is <c>enabled</c>, <c>disabled</c> or <c>failed</c>. <c>Error</c> is a
/// short message, only for a failed plugin. <c>ClientEntry</c> is the URL of the JavaScript module the web UI loads; it
/// is null when the plugin has none or is not enabled.</summary>
public record PluginDto(string Id, string Name, string Version, PluginStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error, string? ClientEntry)
{
    public static PluginDto FromPlugin(InstalledPlugin plugin) =>
        new(plugin.Id, plugin.Manifest.Name, plugin.Manifest.Version, plugin.Status, plugin.Error, plugin.ClientEntryUrl);
}
