using System.Text.Json.Serialization;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services.Storage.Models;

/// <summary>Project settings and metadata. Persistence splits the aggregated documents into their owning folders.</summary>
public sealed class ProjectManifest : FolderManifest
{
    public ProjectSettings Settings { get; set; } = new();
    // Services see project-relative documents; only the persistence layer splits them by owner.
    [JsonIgnore] internal Dictionary<string, FolderManifest> FolderManifests { get; set; } = [];

    internal ProjectManifest Clone() => new()
    {
        Id = Id, Version = Version, Settings = Settings.Clone(),
        PinnedView = PinnedView, ItemOrder = [.. ItemOrder], Threads = [.. Threads], ThreadAxis = ThreadAxis,
        Documents = Documents.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        Folders = Folders.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        FolderManifests = FolderManifests.ToDictionary(pair => pair.Key, pair => pair.Value.CloneFolder()),
        Extra = Extra is null ? null : new(Extra),
    };
}
