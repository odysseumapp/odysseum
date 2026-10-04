using Odysseum.Abstractions.Views;

namespace Odysseum.Abstractions.Plugins;

/// <summary>What a plugin can add to the server. The server implements it; plugins only call it, so it can get new
/// members without a change to existing plugins.</summary>
public interface IPluginRegistry
{
    /// <summary>Adds a view. A view whose name another plugin or the server already uses is not added. The server checks
    /// the view when <see cref="IPlugin.Register"/> returns: a view without a label, or whose client entry or icon is not
    /// a file in the plugin's <c>wwwroot</c> folder, makes the plugin fail.</summary>
    void AddView(IViewDefinition view);

    /// <summary>Adds a view; see <see cref="AddView(IViewDefinition)"/>. <paramref name="clientEntry"/> and
    /// <paramref name="icon"/> are relative to the plugin's <c>wwwroot</c> folder.</summary>
    void AddView(string name, string label, string clientEntry, string? icon = null, IReadOnlyList<string>? folderSettings = null) =>
        AddView(new ViewDefinition(name, label, clientEntry, icon, folderSettings ?? []));
}
