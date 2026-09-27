using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Services.Views;

/// <summary>The known views. It handles the settings that hold folder IDs; the server core stores all other settings
/// without reading them. A view that is not known here is still stored.</summary>
public sealed class ViewCatalog
{
    public static ViewCatalog Default { get; } = new(DefaultViews.All);

    private readonly Dictionary<string, IViewDefinition> _views;

    public ViewCatalog(IEnumerable<IViewDefinition> views)
    {
        _views = new(StringComparer.Ordinal);
        foreach (var view in views)
        {
            ViewNames.Check(view.Name);
            _views[view.Name] = view;
        }
    }

    public IReadOnlyCollection<IViewDefinition> Views => _views.Values;

    /// <summary>Each folder setting in the given settings must be null or the ID of a folder of the project.</summary>
    public void CheckFolders(Project project, IReadOnlyDictionary<string, JsonElement>? views)
    {
        foreach (var (name, settings) in views ?? new Dictionary<string, JsonElement>())
        {
            if (ViewNames.Removes(settings) || settings.ValueKind != JsonValueKind.Object || !_views.TryGetValue(name, out var view)) continue;
            foreach (var key in view.FolderSettings)
            {
                if (!settings.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                if (value.ValueKind != JsonValueKind.String || project.Folder(value.GetString()!) is null)
                    throw new WorkspaceException(WorkspaceError.Invalid, $"The '{name}' view's {key} setting names a folder that does not exist.");
            }
        }
    }

    /// <summary>The changes that remove every folder setting naming the folder: a view whose settings become empty gets a
    /// JSON null. Empty when no setting names it.</summary>
    public Dictionary<string, JsonElement> WithoutFolder(IReadOnlyDictionary<string, JsonElement> views, string folderId) =>
        MapFolders(views, id => id == folderId ? null : id, changedOnly: true);

    /// <summary>The settings with each folder setting passed through <paramref name="map"/>. A setting the map returns null
    /// for is removed. With <paramref name="changedOnly"/>, only the views that changed are returned.</summary>
    public Dictionary<string, JsonElement> MapFolders(IReadOnlyDictionary<string, JsonElement> views, Func<string, string?> map, bool changedOnly = false)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, settings) in views)
        {
            if (settings.ValueKind != JsonValueKind.Object || !_views.TryGetValue(name, out var view) || view.FolderSettings.Count == 0)
            {
                if (!changedOnly) result[name] = settings;
                continue;
            }
            var node = JsonNode.Parse(settings.GetRawText())!.AsObject();
            var changed = false;
            foreach (var key in view.FolderSettings)
            {
                if (node[key] is not JsonValue value || !value.TryGetValue<string>(out var id)) continue;
                var mapped = map(id);
                if (mapped == id) continue;
                if (mapped is null) node.Remove(key);
                else node[key] = mapped;
                changed = true;
            }
            if (changedOnly && !changed) continue;
            result[name] = node.Count == 0 && changedOnly ? JsonSerializer.SerializeToElement<JsonNode?>(null) : JsonSerializer.SerializeToElement(node);
        }
        return result;
    }
}
