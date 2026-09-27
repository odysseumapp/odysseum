using System.Text.Json.Serialization;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Repositories.Manifests;

public sealed class ProjectManifest : FolderManifest
{
    public ProjectSettings Settings { get; set; } = new();
    [JsonIgnore] internal Dictionary<string, FolderManifest> FolderManifests { get; set; } = [];
    /// <summary>The contents of <c>links.json</c>, kept beside <c>project.json</c>.</summary>
    [JsonIgnore] internal LinkManifest Links { get; set; } = new();

    internal ProjectManifest Clone() => new()
    {
        Id = Id, Version = Version, Settings = Settings.Clone(),
        PinnedView = PinnedView, ItemOrder = [.. ItemOrder], Views = Views is null ? null : new(Views), GridFolder = GridFolder,
        Documents = Documents.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        Folders = Folders.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        FolderManifests = FolderManifests.ToDictionary(pair => pair.Key, pair => pair.Value.CloneFolder()),
        Links = Links.Clone(),
        Extra = Extra is null ? null : new(Extra),
    };
}
