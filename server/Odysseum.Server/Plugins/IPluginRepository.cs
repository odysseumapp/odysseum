namespace Odysseum.Server.Plugins;

/// <summary>The plugins found at startup.</summary>
public interface IPluginRepository
{
    /// <summary>Every plugin with a valid manifest, enabled or not, by folder name.</summary>
    IReadOnlyList<InstalledPlugin> GetAll();
}
