using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Services.Storage.Models;

/// <summary>Metadata for immediate children of a content folder. Paths are local names.</summary>
public class FolderManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Version { get; set; } = 1;
    public string? PinnedView { get; set; }
    public string[] ItemOrder { get; set; } = [];
    /// <summary>The folder's grid: <c>Rows</c> are document ids; <c>Columns</c> are document ids, folder ids, or
    /// <c>folderId/*</c> for every document under a folder in order (empty means this folder's children);
    /// <c>Axis</c> says whether the rows run as rows (default) or columns.</summary>
    public string[] Rows { get; set; } = [];
    public string[] Columns { get; set; } = [];
    public string? Axis { get; set; }
    public Dictionary<string, DocumentMetadata> Documents { get; set; } = [];
    public Dictionary<string, FolderEntry> Folders { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal FolderManifest CloneFolder() => new()
    {
        Id = Id, Version = Version,
        PinnedView = PinnedView, ItemOrder = [.. ItemOrder], Rows = [.. Rows], Columns = [.. Columns], Axis = Axis,
        Documents = Documents.ToDictionary(p => p.Key, p => p.Value.Clone()),
        Folders = Folders.ToDictionary(p => p.Key, p => p.Value.Clone()),
        Extra = Extra is null ? null : new(Extra),
    };
}

public sealed class FolderEntry
{
    public string Path { get; set; } = "";
    public double Order { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    internal FolderEntry Clone() => new() { Path = Path, Order = Order, Extra = Extra is null ? null : new(Extra) };
}
