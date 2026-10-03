using Odysseum.Abstractions.Plugins;
using Odysseum.Abstractions.Views;

namespace Odysseum.Plugins.Views;

/// <summary>The views Odysseum ships with besides the editor: a board, an outline and a grid. It loads like any other
/// plugin; its id, name, version and client entry are in plugin.json.</summary>
public sealed class ViewsPlugin : IPlugin
{
    public void Register(IPluginRegistry registry)
    {
        registry.AddView(new ViewDefinition("board"));
        registry.AddView(new ViewDefinition("outline"));
        // columnFolder: the folder whose documents are the grid's columns. Missing means the client's default.
        registry.AddView(new ViewDefinition("grid", ["columnFolder"]));
    }
}
