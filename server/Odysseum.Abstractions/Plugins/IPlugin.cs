namespace Odysseum.Abstractions.Plugins;

/// <summary>The entry point of a plugin. A plugin is a folder in the server's plugins folder with a <c>plugin.json</c>
/// manifest, which gives the plugin's id, name, version, assembly and client entry. The server loads the assembly,
/// makes one instance of its <see cref="IPlugin"/> type with the parameterless constructor, and calls
/// <see cref="Register"/> once at startup.</summary>
public interface IPlugin
{
    /// <summary>Tells the server what the plugin adds.</summary>
    void Register(IPluginRegistry registry);
}
