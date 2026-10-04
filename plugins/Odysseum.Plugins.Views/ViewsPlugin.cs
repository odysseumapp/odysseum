using Odysseum.Abstractions.Plugins;

namespace Odysseum.Plugins.Views;

/// <summary>The views Odysseum ships with besides the editor: a board, an outline and a grid. It loads like any other
/// plugin; its id, name and version are in plugin.json. The client files come from odysseum-web/views-plugin.</summary>
public sealed class ViewsPlugin : IPlugin
{
    public void Register(IPluginRegistry registry)
    {
        registry.AddView(name: "board", label: "Corkboard", clientEntry: "board.js", icon: "board.svg");
        registry.AddView(name: "outline", label: "Outline", clientEntry: "outline.js", icon: "outline.svg");
        // columnFolder: the folder whose documents are the grid's columns. Missing means the client's default.
        registry.AddView(name: "grid", label: "Grid", clientEntry: "grid.js", icon: "grid.svg", folderSettings: ["columnFolder"]);
    }
}
