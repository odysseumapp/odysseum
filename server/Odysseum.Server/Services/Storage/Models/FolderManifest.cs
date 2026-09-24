using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Services.Storage.Models;

public class FolderManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Version { get; set; } = 1;
    public string? PinnedView { get; set; }
    public string[] ItemOrder { get; set; } = [];
    public string? GridFolder { get; set; }
    public Dictionary<string, DocumentMetadata> Documents { get; set; } = [];
    public Dictionary<string, FolderEntry> Folders { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal FolderManifest CloneFolder() => new()
    {
        Id = Id, Version = Version,
        PinnedView = PinnedView, ItemOrder = [.. ItemOrder], GridFolder = GridFolder,
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
