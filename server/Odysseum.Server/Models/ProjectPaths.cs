namespace Odysseum.Server.Models;

/// <summary>Paths relative to a project use '/'. The project's top folder has the empty path.</summary>
public static class ProjectPaths
{
    public static string Join(string parentPath, string name) => parentPath.Length == 0 ? name : parentPath + "/" + name;

    public static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "";

    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>True when <paramref name="path"/> is <paramref name="folderPath"/> or is inside it.</summary>
    public static bool IsInside(string path, string folderPath) =>
        folderPath.Length == 0 || path == folderPath || path.StartsWith(folderPath + "/", StringComparison.Ordinal);
}
