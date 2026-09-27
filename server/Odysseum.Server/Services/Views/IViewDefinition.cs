namespace Odysseum.Server.Services.Views;

/// <summary>What the server must know about one view: its name, and which of its settings hold a folder ID. The server
/// checks those settings, clears them when the folder is removed, and turns them into folder paths in project templates.
/// Everything else about a view belongs to the client that draws it.</summary>
public interface IViewDefinition
{
    string Name { get; }
    /// <summary>Settings (top-level keys of the view's settings object) whose value is a folder ID.</summary>
    IReadOnlyList<string> FolderSettings { get; }
}

public sealed record ViewDefinition(string Name, IReadOnlyList<string> FolderSettings) : IViewDefinition
{
    public ViewDefinition(string name) : this(name, []) { }
}
