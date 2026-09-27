using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Repositories.Disk.Formats;

/// <summary><c>&lt;folder&gt;/.odysseum/folder.json</c>: the folder's ID, its layout, the order of its children, and the
/// details of the documents in it. The ID is also in <c>folders.json</c>; the copy here lets the server find the folder
/// again when another program moves it.</summary>
public class FolderFile
{
    public const int CurrentVersion = 3;

    [JsonPropertyOrder(-3)] public string Id { get; set; } = "";
    [JsonPropertyOrder(-2)] public int Version { get; set; } = CurrentVersion;
    public string? PinnedView { get; set; }
    /// <summary>Settings by view name. Omitted when no view has settings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, JsonElement>? Views { get; set; }
    /// <summary>The IDs of the subfolders and documents, in order.</summary>
    public List<string> ItemOrder { get; set; } = [];
    /// <summary>The details of the documents in this folder, by document ID.</summary>
    public Dictionary<string, DocumentEntry> Documents { get; set; } = [];
}
