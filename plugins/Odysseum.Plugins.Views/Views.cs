using Odysseum.Abstractions.Views;

namespace Odysseum.Plugins.Views;

// The views Odysseum ships with besides the editor. The plugin loads like any other; its id, name and version are in
// plugin.json. The server finds these classes by themselves and shows the views in this order. The client files come
// from odysseum-web/views-plugin.

/// <summary>The folder's documents and folders as cards in a grid.</summary>
public sealed class BoardView : IViewDefinition
{
    public string Name => "board";
    public string Label => "Corkboard";
    public string ClientEntry => "board.js";
    public string Icon => "board.svg";
}

/// <summary>The folder's documents and folders as one compact list.</summary>
public sealed class OutlineView : IViewDefinition
{
    public string Name => "outline";
    public string Label => "Outline";
    public string ClientEntry => "outline.js";
    public string Icon => "outline.svg";
}

/// <summary>The folder's documents against the documents of another folder, with their links.</summary>
public sealed class GridView : IViewDefinition
{
    public string Name => "grid";
    public string Label => "Grid";
    public string ClientEntry => "grid.js";
    public string Icon => "grid.svg";
    /// <summary>columnFolder: the folder whose documents are the grid's columns. Missing means the client's default.</summary>
    public IReadOnlyList<string> FolderSettings => ["columnFolder"];
}
