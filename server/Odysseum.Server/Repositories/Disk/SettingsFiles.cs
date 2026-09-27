using System.Text.Json;
using System.Text.Json.Serialization;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Repositories.Disk.Formats;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Views;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Repositories.Disk;

/// <summary>Reads, checks and writes the settings files of a project: <c>project.json</c>, <c>documents.json</c>,
/// <c>folders.json</c>, <c>links.json</c> and one <c>folder.json</c> in each folder. A file that is not valid, or that has
/// another version, is refused.</summary>
internal static class SettingsFiles
{
    public const string ProjectFilePath = ".odysseum/project.json";
    public const string LinksFilePath = ".odysseum/links.json";
    public const string DocumentPlacesFilePath = ".odysseum/documents.json";
    public const string FolderPlacesFilePath = ".odysseum/folders.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public static string FolderFilePath(string folderPath) => folderPath + "/.odysseum/folder.json";

    /// <summary>The settings file of a folder. For the project's top folder, this is <c>project.json</c>.</summary>
    public static string PathOf(string folderPath) => folderPath.Length == 0 ? ProjectFilePath : FolderFilePath(folderPath);

    public static byte[] Serialize(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Json);
        if (bytes.Length > FileManager.MaxFileBytes) throw new WorkspaceException(WorkspaceError.TooLarge, "A settings file would exceed the 4 MB limit.");
        return bytes;
    }

    /// <summary>The folder's settings file and its bytes, or nulls when the file does not exist. For the project's top
    /// folder, the file is a <see cref="ProjectFile"/>.</summary>
    public static async Task<(FolderFile? File, byte[]? Bytes)> ReadFolderFileAsync(IFileManager files, string folderPath)
    {
        if (folderPath.Length == 0) return await ReadProjectFileAsync(files);
        var path = FolderFilePath(folderPath);
        if (!files.Exists(path, metadata: true)) return (null, null);
        var bytes = await files.ReadAsync(path, metadata: true);
        var file = Parse<FolderFile>(bytes, path);
        Check(file, path, FolderFile.CurrentVersion);
        return (file, bytes);
    }

    public static async Task<(ProjectFile? File, byte[]? Bytes)> ReadProjectFileAsync(IFileManager files)
    {
        if (!files.Exists(ProjectFilePath, metadata: true)) return (null, null);
        var bytes = await files.ReadAsync(ProjectFilePath, metadata: true);
        var file = Parse<ProjectFile>(bytes, ProjectFilePath);
        Check(file, ProjectFilePath, ProjectFile.CurrentVersion);
        if (file.Settings is null || !ProjectSettings.TryValidate(file.Settings, out _)) throw Invalid(ProjectFilePath);
        return (file, bytes);
    }

    /// <summary>The links file and its bytes. A missing file gives an empty list and null bytes.</summary>
    public static async Task<(LinksFile File, byte[]? Bytes)> ReadLinksFileAsync(IFileManager files)
    {
        if (!files.Exists(LinksFilePath, metadata: true)) return (new LinksFile(), null);
        var bytes = await files.ReadAsync(LinksFilePath, metadata: true);
        var file = Parse<LinksFile>(bytes, LinksFilePath);
        if (file.Version != LinksFile.CurrentVersion || file.Links is null) throw Invalid(LinksFilePath);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in file.Links)
        {
            if (link is null || !IsId(link.Id) || !ids.Add(link.Id) || !IsId(link.FirstDocumentId) || !IsId(link.SecondDocumentId)
                || link.FirstDocumentId == link.SecondDocumentId) throw Invalid(LinksFilePath);
            link.Note ??= "";
        }
        return (file, bytes);
    }

    /// <summary>A places file (<c>documents.json</c> or <c>folders.json</c>) and its bytes. A missing file gives an empty
    /// list and null bytes.</summary>
    public static async Task<(PlacesFile File, byte[]? Bytes)> ReadPlacesFileAsync(IFileManager files, string path)
    {
        if (!files.Exists(path, metadata: true)) return (new PlacesFile(), null);
        var bytes = await files.ReadAsync(path, metadata: true);
        var file = Parse<PlacesFile>(bytes, path);
        if (file.Version != PlacesFile.CurrentVersion || file.Paths is null) throw Invalid(path);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, place) in file.Paths)
            if (!IsId(id) || !FileManager.IsSafePath(place) || !paths.Add(place)) throw Invalid(path);
        return (file, bytes);
    }

    private static T Parse<T>(byte[] bytes, string path)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw Invalid(path); }
        catch (JsonException) { throw Invalid(path); }
    }

    private static void Check(FolderFile file, string path, int version)
    {
        if (file.Version != version || !IsId(file.Id) || file.ItemOrder is null || file.Documents is null
            || file.ItemOrder.Any(string.IsNullOrWhiteSpace) || file.ItemOrder.Distinct().Count() != file.ItemOrder.Count
            || (file.PinnedView is not null && !ViewNames.IsValid(file.PinnedView))
            || (file.Views?.Any(view => !ViewNames.IsValid(view.Key) || view.Value.ValueKind != JsonValueKind.Object) ?? false))
            throw Invalid(path);
        foreach (var (id, entry) in file.Documents)
        {
            if (!IsId(id) || entry is null || entry.Title is null || entry.Synopsis is null
                || entry.Notes is null || !Enum.IsDefined(entry.Status) || entry.WordGoal is < 0 or > 10000000)
                throw Invalid(path);
        }
        if (file.Views is { Count: 0 }) file.Views = null;
    }

    private static bool IsId(string? value) => Guid.TryParseExact(value, "D", out _);

    private static WorkspaceException Invalid(string path) =>
        new(WorkspaceError.Corrupt, $"The file {path} is not valid or has an unsupported version. Fix it, or restore it from a version.");
}
