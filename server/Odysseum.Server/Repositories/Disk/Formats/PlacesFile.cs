namespace Odysseum.Server.Repositories.Disk.Formats;

/// <summary><c>&lt;project&gt;/.odysseum/documents.json</c> and <c>&lt;project&gt;/.odysseum/folders.json</c>: the path of
/// each document or folder of the project, by ID. The paths are relative to the project. The project's top folder is not
/// listed; its ID is the project ID.</summary>
public sealed class PlacesFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, string> Paths { get; set; } = [];
}
