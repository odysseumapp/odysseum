namespace Odysseum.Server.Plugins;

public enum PluginStatus
{
    /// <summary>The plugin loaded and registered.</summary>
    Enabled,
    /// <summary>The plugin's id is in <c>disabledPlugins</c>; its code was not loaded.</summary>
    Disabled,
    /// <summary>The plugin's code could not be loaded, or its registration failed.</summary>
    Failed,
}
