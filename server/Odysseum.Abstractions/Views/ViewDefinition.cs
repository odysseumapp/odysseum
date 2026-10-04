namespace Odysseum.Abstractions.Views;

/// <summary>A view with its name, label, client files and the settings that hold a folder ID.</summary>
public sealed record ViewDefinition(string Name, string Label, string? ClientEntry, string? Icon, IReadOnlyList<string> FolderSettings)
    : IViewDefinition;
