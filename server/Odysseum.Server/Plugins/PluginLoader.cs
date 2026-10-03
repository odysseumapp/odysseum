using Odysseum.Abstractions.Plugins;
using Odysseum.Abstractions.Views;

namespace Odysseum.Server.Plugins;

/// <summary>Reads each plugin folder's manifest, then loads the enabled plugins, each into its own
/// <see cref="PluginLoadContext"/>, finds its <see cref="IPlugin"/> type by reflection, and lets it register. A plugin is
/// code from outside the server, so any error it causes is logged, the plugin gets <see cref="PluginStatus.Failed"/>,
/// and the server starts without it.</summary>
public sealed class PluginLoader(ILogger? logger = null) : IPluginLoader
{
    public IReadOnlyList<InstalledPlugin> Load(string folder, IEnumerable<string> disabledPlugins)
    {
        if (!Directory.Exists(folder))
        {
            logger?.LogInformation("No plugins folder at {Folder}; no plugins are loaded.", folder);
            return [];
        }
        var disabled = disabledPlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plugins = new List<InstalledPlugin>();
        foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            PluginManifest manifest;
            try { manifest = PluginManifest.Read(directory); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                logger?.LogError("The plugin folder {Folder} is skipped: {Reason}", directory, ex.Message);
                continue;
            }
            if (plugins.Any(plugin => string.Equals(plugin.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
            {
                logger?.LogError("The plugin folder {Folder} is skipped: another plugin has the id '{Id}'.", directory, manifest.Id);
                continue;
            }
            if (disabled.Contains(manifest.Id))
            {
                logger?.LogInformation("Plugin {Id} is disabled.", manifest.Id);
                plugins.Add(new InstalledPlugin(manifest, directory, PluginStatus.Disabled, null, []));
                continue;
            }
            try
            {
                var views = Register(directory, manifest);
                plugins.Add(new InstalledPlugin(manifest, directory, PluginStatus.Enabled, null, views));
                logger?.LogInformation("Loaded plugin {Id} {Version} with views: {Views}.", manifest.Id, manifest.Version,
                    views.Count == 0 ? "none" : string.Join(", ", views.Select(view => view.Name)));
            }
            catch (Exception ex)
            {
                // The log gets the full .NET error; the API gets only the short message, which has no file paths.
                logger?.LogError(ex, "Plugin {Id} could not be loaded and is not enabled.", manifest.Id);
                var error = ex is PluginLoadException ? ex.Message : "The plugin could not be loaded.";
                plugins.Add(new InstalledPlugin(manifest, directory, PluginStatus.Failed, error, []));
            }
        }
        return plugins;
    }

    /// <summary>Loads the plugin's assembly and calls <see cref="IPlugin.Register"/>. Returns the views it added. Each
    /// step that fails throws a <see cref="PluginLoadException"/> with a short message.</summary>
    private static IReadOnlyList<IViewDefinition> Register(string directory, PluginManifest manifest)
    {
        var assemblyPath = Path.Combine(directory, manifest.Assembly);
        Type[] types;
        try
        {
            var assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
            types = assembly.GetTypes().Where(type => typeof(IPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false }).ToArray();
        }
        catch (Exception ex) { throw new PluginLoadException("The plugin's assembly could not be loaded.", ex); }
        if (types.Length != 1) throw new PluginLoadException($"The plugin's assembly must have exactly one IPlugin type; it has {types.Length}.");
        IPlugin plugin;
        try { plugin = (IPlugin)Activator.CreateInstance(types[0])!; }
        catch (Exception ex) { throw new PluginLoadException("The plugin could not be created.", ex); }
        var registry = new PluginRegistry();
        try { plugin.Register(registry); }
        catch (Exception ex) { throw new PluginLoadException($"Register failed: {ex.Message}", ex); }
        return registry.Views;
    }

    /// <summary>A plugin failed to load. <c>Message</c> is short and safe to show; the inner exception has the details.</summary>
    private sealed class PluginLoadException(string message, Exception? inner = null) : Exception(message, inner);
}
