using Odysseum.Abstractions.Views;

namespace Odysseum.Server.Plugins;

/// <summary>A plugin folder with a valid manifest. A plugin that is not <see cref="PluginStatus.Enabled"/> has no views,
/// and its files are not served. <c>Error</c> says why a <see cref="PluginStatus.Failed"/> plugin failed, and is null
/// otherwise.</summary>
public sealed record InstalledPlugin(PluginManifest Manifest, string Folder, PluginStatus Status, string? Error, IReadOnlyList<IViewDefinition> Views)
{
    public string Id => Manifest.Id;

    public bool Enabled => Status == PluginStatus.Enabled;

    /// <summary>The folder of the plugin's client files, served at <c>/plugins/{id}/</c> when the plugin is enabled.</summary>
    public string WwwRoot => Path.Combine(Folder, "wwwroot");

    /// <summary>The URL of the client entry, or null when there is none or the plugin is not enabled.</summary>
    public string? ClientEntryUrl => Enabled && Manifest.ClientEntry is { } entry ? $"/plugins/{Id}/{entry}" : null;
}
