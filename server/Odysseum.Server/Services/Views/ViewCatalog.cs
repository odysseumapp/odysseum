using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Views;

namespace Odysseum.Server.Services.Views;

/// <summary>The known views: the core's <see cref="WriteView"/>, and the views that plugins add. It handles the
/// settings that hold folder IDs; the server core stores all other settings without reading them. Settings of a view
/// that is not known here are still stored.</summary>
public sealed class ViewCatalog
{
    /// <summary>The editor. It is always known, also when no plugin is loaded.</summary>
    public const string WriteView = "write";

    private readonly Dictionary<string, IViewDefinition> _views = new(StringComparer.Ordinal) { [WriteView] = new ViewDefinition(WriteView, "Write", null, null, []) };

    /// <summary>The core view and the given views. A view with a name that is not allowed or that is already known is
    /// logged and left out.</summary>
    public ViewCatalog(IEnumerable<IViewDefinition> views, ILogger? logger = null)
    {
        foreach (var view in views)
        {
            if (!ViewNames.IsValid(view.Name)) logger?.LogError("The view name '{View}' is not allowed; the view is skipped.", view.Name);
            else if (!_views.TryAdd(view.Name, view)) logger?.LogError("Two views are called '{View}'; the second one is skipped.", view.Name);
        }
    }

    public IReadOnlyCollection<IViewDefinition> Views => _views.Values;

    /// <summary>Each folder setting in the given settings must be null or the ID of a folder of the project.</summary>
    public void CheckFolders(Func<string, bool> folderExists, IReadOnlyDictionary<string, JsonElement>? views)
    {
        foreach (var (name, settings) in views ?? new Dictionary<string, JsonElement>())
        {
            if (ViewNames.Removes(settings) || settings.ValueKind != JsonValueKind.Object || !_views.TryGetValue(name, out var view)) continue;
            foreach (var key in view.FolderSettings)
            {
                if (!settings.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) continue;
                if (value.ValueKind != JsonValueKind.String || !folderExists(value.GetString()!))
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
