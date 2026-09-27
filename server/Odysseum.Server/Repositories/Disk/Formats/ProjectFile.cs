using System.Text.Json.Serialization;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Repositories.Disk.Formats;

/// <summary><c>&lt;project&gt;/.odysseum/project.json</c>: the project's ID and settings, and the same values as a
/// <see cref="FolderFile"/> for the project's top folder. The project ID is also the top folder's ID.</summary>
public sealed class ProjectFile : FolderFile
{
    public new const int CurrentVersion = 4;

    public ProjectFile() => Version = CurrentVersion;

    [JsonPropertyOrder(-1)] public ProjectSettings Settings { get; set; } = new();
}
