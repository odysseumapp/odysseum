namespace Odysseum.Abstractions.Views;

/// <summary>One view. The server uses <see cref="FolderSettings"/>: it checks those settings, clears them when the folder
/// is deleted, and turns them into folder paths in project templates. It gives the other members to the web UI, which
/// shows the view in its view selector and loads <see cref="ClientEntry"/> when the view is selected. Everything else
/// about a view belongs to the component that draws it.</summary>
public interface IViewDefinition
{
    /// <summary>Lowercase letters, digits, '.', '-' and '_', starting with a letter or digit, at most 64 characters.</summary>
    string Name { get; }
    /// <summary>The name the view selector shows.</summary>
    string Label { get; }
    /// <summary>The JavaScript module that draws the view, relative to the plugin's <c>wwwroot</c> folder. Its default
    /// export is the view's Vue component. A CSS file with the same name next to it is loaded with it. Null only for the
    /// core's editor, which the web UI draws itself.</summary>
    string? ClientEntry { get; }
    /// <summary>An SVG file relative to the plugin's <c>wwwroot</c> folder, or null for no icon. The web UI paints its
    /// shape in the text color, so the icon follows the theme.</summary>
    string? Icon { get; }
    /// <summary>Settings (top-level keys of the view's settings object) whose value is a folder ID.</summary>
    IReadOnlyList<string> FolderSettings { get; }
}
