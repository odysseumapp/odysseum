namespace Odysseum.Server.Services.Projects;

/// <summary>The folders every new project starts with, in sidebar order.</summary>
public static class DefaultFolders
{
    public static readonly string[] Names = ["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"];

    public static bool IsDefaultFolder(string path) => Names.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>The position of a default folder name, or <see cref="int.MaxValue"/> for any other name.</summary>
    public static int Rank(string name)
    {
        var index = Array.FindIndex(Names, folder => string.Equals(folder, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }
}
