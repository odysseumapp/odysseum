namespace Odysseum.Server.Plugins;

/// <summary>Loads the plugins from a plugins folder.</summary>
public interface IPluginLoader
{
    /// <summary>Reads <c>plugin.json</c> in each subfolder, and loads each plugin whose id is not in
    /// <paramref name="disabledPlugins"/>. A folder without a valid manifest is logged and skipped. The result has every
    /// plugin with a manifest, also the disabled ones.</summary>
    IReadOnlyList<InstalledPlugin> Load(string folder, IEnumerable<string> disabledPlugins);
}
