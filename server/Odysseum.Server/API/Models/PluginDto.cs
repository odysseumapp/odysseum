using System.Text.Json.Serialization;
using Odysseum.Server.Plugins;

namespace Odysseum.Server.API.Models;

/// <summary>An installed plugin. <c>Status</c> is <c>enabled</c>, <c>disabled</c> or <c>failed</c>. <c>Error</c> is a
/// short message, only for a failed plugin. <c>Views</c> are the views the plugin added; they are empty when the plugin
/// is not enabled.</summary>
public record PluginDto(string Id, string Name, string Version, PluginStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error, IReadOnlyList<PluginViewDto> Views)
{
    public static PluginDto FromPlugin(InstalledPlugin plugin) =>
        new(plugin.Id, plugin.Manifest.Name, plugin.Manifest.Version, plugin.Status, plugin.Error,
            [.. plugin.Views.Select(view => new PluginViewDto(view.Name, view.Label, plugin.ClientUrl(view.ClientEntry!),
                view.Icon is null ? null : plugin.ClientUrl(view.Icon)))]);
}

/// <summary>A view as the web UI needs it. <c>ClientEntry</c> is the URL of the JavaScript module whose default export is
/// the view's component; <c>Icon</c> is the URL of an SVG file, or null.</summary>
public record PluginViewDto(string Name, string Label, string ClientEntry, string? Icon);
