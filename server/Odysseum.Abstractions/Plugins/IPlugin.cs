namespace Odysseum.Abstractions.Plugins;

/// <summary>The optional setup class of a plugin. A plugin is a folder in the server's plugins folder with a
/// <c>plugin.json</c> manifest, which gives the plugin's id, name, version and assembly. The server loads the assembly and
/// finds what the plugin adds by its classes, for example each <see cref="Views.IViewDefinition"/>. An assembly can also
/// have one <see cref="IPlugin"/> class: the server makes one instance with the parameterless constructor and calls
/// <see cref="Register"/> once at startup.</summary>
public interface IPlugin
{
    /// <summary>Sets up what the plugin needs before the server starts.</summary>
    void Register(IPluginRegistry registry);
}
