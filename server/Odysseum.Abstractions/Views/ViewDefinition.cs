namespace Odysseum.Abstractions.Views;

/// <summary>A view with its name and the settings that hold a folder ID.</summary>
public sealed record ViewDefinition(string Name, IReadOnlyList<string> FolderSettings) : IViewDefinition
{
    /// <summary>A view without folder settings.</summary>
    public ViewDefinition(string name) : this(name, []) { }
}
