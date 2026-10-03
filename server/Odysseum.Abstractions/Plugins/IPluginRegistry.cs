using Odysseum.Abstractions.Views;

namespace Odysseum.Abstractions.Plugins;

/// <summary>What a plugin can add to the server. The server implements it; plugins only call it, so it can get new
/// members without a change to existing plugins.</summary>
public interface IPluginRegistry
{
    /// <summary>Adds a view. A view whose name another plugin or the server already uses is not added.</summary>
    void AddView(IViewDefinition view);
}
