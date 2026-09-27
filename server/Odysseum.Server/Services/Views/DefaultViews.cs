namespace Odysseum.Server.Services.Views;

/// <summary>The views the web UI ships with. This is the built-in views module; a plugin will add views the same way.</summary>
public static class DefaultViews
{
    public static IReadOnlyList<IViewDefinition> All { get; } =
    [
        new ViewDefinition("write"),
        new ViewDefinition("board"),
        new ViewDefinition("outline"),
        // columnFolder: the folder whose documents are the grid's columns. Missing means the client's default.
        new ViewDefinition("grid", ["columnFolder"]),
    ];
}
