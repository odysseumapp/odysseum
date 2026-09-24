using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Repositories.Files;

public sealed class FileManager(string root) : IFileManager
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const string MetadataDirectory = ".odysseum";
    private const string LegacyMetadataDirectory = ".writer";
    public string Root { get; } = Path.GetFullPath(root);

    public FileStream AcquireInstanceLock()
    {
        Directory.CreateDirectory(Root);
        AssertNoLinks(Root);
        try
        {
            MigrateLegacyMetadata();
            Directory.CreateDirectory(ResolvePath(MetadataDirectory, true));
            return new FileStream(ResolvePath(".odysseum/instance.lock", true), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new WorkspaceException(WorkspaceError.Unavailable, "Another Odysseum instance is already using this project.");
        }
    }

    private void MigrateLegacyMetadata()
    {
        foreach (var folder in EnumerateFolders().Prepend("").ToArray())
        {
            var directory = folder == "" ? Root : ResolvePath(folder);
            var legacy = Path.Combine(directory, LegacyMetadataDirectory);
            if (!Directory.Exists(legacy) || Directory.Exists(Path.Combine(directory, MetadataDirectory))) continue;
            AssertNoLinks(legacy);
            Directory.Move(legacy, Path.Combine(directory, MetadataDirectory));
        }
    }

    public bool Exists(string relative, bool metadata = false) => File.Exists(ResolvePath(relative, metadata));
    public bool FolderExists(string relative) => Directory.Exists(RootOr(relative));
    public DateTime LastModified(string relative, bool metadata = false) => File.GetLastWriteTimeUtc(RootOr(relative, metadata));
    public Task<byte[]> ReadAsync(string relative, bool metadata = false) => ReadBytesAsync(ResolvePath(relative, metadata));

    public Task WriteAsync(string relative, byte[] bytes, bool overwrite = true, bool metadata = false)
    {
        var path = ResolvePath(relative, metadata);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return AtomicWriteAsync(path, bytes, overwrite);
    }

    public void Move(string source, string destination)
    {
        var from = ResolvePath(source);
        var to = ResolvePath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
    }

    public void Delete(string relative, bool metadata = false) => File.Delete(ResolvePath(relative, metadata));

    public FileStream Lock(string relative, bool metadata = false) => new(ResolvePath(relative, metadata), FileMode.Open,
        FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);

    public IEnumerable<string> EnumerateDocuments() => EnumerateDocuments(Root);

    public IEnumerable<string> EnumerateFolders(bool recursive = true) => EnumerateFolders(Root, recursive);

    public IEnumerable<string> EnumerateFiles(string relative, string pattern, bool metadata = false)
    {
        var directory = RootOr(relative, metadata);
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, pattern))
        {
            AssertNoLinks(path);
            yield return Path.GetRelativePath(Root, path).Replace('\\', '/');
        }
    }

    public void CreateFolder(string relative)
    {
        var path = ResolvePath(relative);
        if (File.Exists(path)) throw new WorkspaceException(WorkspaceError.Conflict, "A file already has that name.");
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) throw new WorkspaceException(WorkspaceError.NotFound, "The parent folder no longer exists.");
        Directory.CreateDirectory(path);
    }

    public void RemoveEmptyFolder(string relative)
    {
        var path = ResolvePath(relative);
        if (!Directory.Exists(path)) return;
        if (Directory.EnumerateFileSystemEntries(path).Any(entry => Path.GetFileName(entry) is var name && name != ".odysseum" && name != $".{Path.GetFileName(path)}.md"))
            throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be removed. Move their files and subfolders first.");
        var destination = ResolvePath(".odysseum/removed-folders/" + Guid.NewGuid().ToString("N"), true);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(path, destination);
    }

    private IEnumerable<string> EnumerateFolders(string directory, bool recursive)
    {
        AssertNoLinks(directory);
        if (!Directory.Exists(directory)) yield break;
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(child).StartsWith('.') || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            yield return Path.GetRelativePath(Root, child).Replace('\\', '/');
            if (!recursive) continue;
            foreach (var nested in EnumerateFolders(child, true)) yield return nested;
        }
    }

    private IEnumerable<string> EnumerateDocuments(string directory)
    {
        AssertNoLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            var folderDocument = directory != Root && name == $".{Path.GetFileName(directory)}.md";
            if ((name.StartsWith('.') && !folderDocument) || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
            if (Directory.Exists(entry))
            {
                foreach (var child in EnumerateDocuments(entry)) yield return child;
            }
            else if (DocumentRules.IsDocument(entry)) yield return Path.GetRelativePath(Root, entry).Replace('\\', '/');
        }
    }

    private static bool IsRelative(string? relative) =>
        !string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) && !relative.Contains('\\') && !relative.Contains(':');

    private static bool HasSafeSegments(string relative, bool allowMetadata)
    {
        var segments = relative.Split('/');
        return !segments.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.EndsWith('.') || x.EndsWith(' ')
            || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            && (allowMetadata || !segments.Where((x, i) => x.StartsWith('.') && !(i > 0 && i == segments.Length - 1 && x == $".{segments[i - 1]}.md")).Any());
    }

    public static bool IsSafePath(string? relative) => IsRelative(relative) && HasSafeSegments(relative!, allowMetadata: false);

    private string RootOr(string relative, bool allowMetadata = false) => relative == "" ? Root : ResolvePath(relative, allowMetadata);

    private string ResolvePath(string relative, bool allowMetadata = false)
    {
        AssertNoLinks(Root);
        if (!IsRelative(relative)) throw new WorkspaceException(WorkspaceError.Invalid, "Use a relative path inside the workspace.");
        if (!HasSafeSegments(relative, allowMetadata)) throw new WorkspaceException(WorkspaceError.Invalid, "That path is not allowed.");
        var current = Root;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            AssertNoLinks(current);
        }
        return current;
    }

    private static void AssertNoLinks(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException(WorkspaceError.Invalid, "Symbolic links and junctions are not supported inside a workspace.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static async Task<byte[]> ReadBytesAsync(string path)
    {
        var before = new FileInfo(path);
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        if (length > MaxFileBytes) throw new WorkspaceException(WorkspaceError.TooLarge, "File exceeds the 4 MB limit.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > MaxFileBytes) throw new WorkspaceException(WorkspaceError.TooLarge, "File exceeds the 4 MB limit.");
            memory.Write(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (memory.Length != length || after.Length != length || after.LastWriteTimeUtc != modified)
            throw new IOException("The file changed while it was being read.");
        return memory.ToArray();
    }

    private static async Task AtomicWriteAsync(string path, byte[] content, bool overwrite = true)
    {
        AssertNoLinks(path);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".odysseum-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && File.Exists(path)) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
