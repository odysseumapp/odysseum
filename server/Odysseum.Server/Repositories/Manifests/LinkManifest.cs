using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Repositories.Manifests;

/// <summary><c>.odysseum/links.json</c>: every link between two documents of the project, each stored once. Links have
/// no direction; the order of the list is the order in which each document lists its links.</summary>
public sealed class LinkManifest
{
    public int Version { get; set; } = 1;
    public List<LinkEntry> Links { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal LinkManifest Clone() => new() { Version = Version, Links = [.. Links.Select(link => link.Clone())], Extra = Extra is null ? null : new(Extra) };
}

public sealed class LinkEntry
{
    public string ItemA { get; set; } = "";
    public string ItemB { get; set; } = "";
    /// <summary>One note for the link, read from both ends. Empty when there is none.</summary>
    public string Note { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal bool Joins(string id) => ItemA == id || ItemB == id;
    internal bool Joins(string first, string second) => (ItemA == first && ItemB == second) || (ItemA == second && ItemB == first);
    internal string Other(string id) => ItemA == id ? ItemB : ItemA;
    internal LinkEntry Clone() => new() { ItemA = ItemA, ItemB = ItemB, Note = Note, Extra = Extra is null ? null : new(Extra) };
}
