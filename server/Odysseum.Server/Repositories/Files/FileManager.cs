using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Repositories.Disk;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Repositories.Files;

/// <summary>The only class that touches a project's files on disk. Every write is recorded in <see cref="OwnWrites"/>
/// when one is given, so the file watcher can tell the server's own writes from external changes.</summary>
public sealed class FileManager(string root, OwnWrites? ownWrites = null) : IFileManager
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const string MetadataDirectory = ".odysseum";
    public string Root { get; } = Path.GetFullPath(root);

    public FileStream AcquireInstanceLock()
    {
        Directory.CreateDirectory(Root);
        AssertNoLinks(Root);
        try
        {
            Directory.CreateDirectory(ResolvePath(MetadataDirectory, true));
            return new FileStream(ResolvePath(".odysseum/instance.lock", true), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new WorkspaceException(WorkspaceError.Unavailable, "Another Odysseum instance is already using this project.");
        }
    }

    public bool Exists(string relative, bool metadata = false) => File.Exists(ResolvePath(relative, metadata));
    public bool FolderExists(string relative) => Directory.Exists(RootOr(relative));
    public DateTime LastModified(string relative, bool metadata = false) => File.GetLastWriteTimeUtc(RootOr(relative, metadata));
    public Task<byte[]> ReadAsync(string relative, bool metadata = false) => ReadBytesAsync(ResolvePath(relative, metadata));

    public async Task WriteAsync(string relative, byte[] bytes, bool overwrite = true, bool metadata = false)
    {
        var path = ResolvePath(relative, metadata);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Changing(path);
        await AtomicWriteAsync(path, bytes, overwrite);
        Changed(path);
    }

    public void Move(string source, string destination)
    {
        var from = ResolvePath(source);
        var to = ResolvePath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Changing(from);
        Changing(to);
        File.Move(from, to);
        Changed(from);
        Changed(to);
    }

    public void MoveFolder(string source, string destination)
    {
        var from = ResolvePath(source);
        var to = ResolvePath(destination);
        if (!Directory.Exists(from)) throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
        if (Directory.Exists(to) || File.Exists(to)) throw new WorkspaceException(WorkspaceError.Conflict, "A folder or file already has that name.");
        var parent = Path.GetDirectoryName(to)!;
        if (!Directory.Exists(parent)) throw new WorkspaceException(WorkspaceError.NotFound, "The parent folder no longer exists.");
        if (to.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException(WorkspaceError.Invalid, "A folder cannot move into itself.");
        Changing(from);
        Changing(to);
        Directory.Move(from, to);
        Changed(from);
        Changed(to);
    }

    public void Delete(string relative, bool metadata = false)
    {
        var path = ResolvePath(relative, metadata);
        Changing(path);
        File.Delete(path);
        Changed(path);
    }

    public FileStream Lock(string relative, bool metadata = false) => new(ResolvePath(relative, metadata), FileMode.Open,
        FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);

    public IEnumerable<string> EnumerateDocuments() => EnumerateDocuments(Root);

    public IEnumerable<string> EnumerateFolders(bool recursive = true) => EnumerateFolders(Root, recursive);

    /// <summary>The folders directly inside the folder, as paths relative to the root.</summary>
    public IEnumerable<string> EnumerateSubfolders(string relative) => EnumerateFolders(RootOr(relative), recursive: false);

    /// <summary>The documents directly inside the folder, with the folder's own hidden document, as paths relative to the root.</summary>
    public IEnumerable<string> EnumerateDocumentsIn(string relative) =>
        EnumerateDocuments(RootOr(relative), recursive: false).Order(StringComparer.Ordinal);

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
        var path = RootOr(relative);
        if (File.Exists(path)) throw new WorkspaceException(WorkspaceError.Conflict, "A file already has that name.");
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) throw new WorkspaceException(WorkspaceError.NotFound, "The parent folder no longer exists.");
        Changing(path);
        Directory.CreateDirectory(path);
        Changed(path);
    }

    public void RemoveEmptyFolder(string relative)
    {
        var path = ResolvePath(relative);
        if (!Directory.Exists(path)) return;
        if (Directory.EnumerateFileSystemEntries(path).Any(entry => Path.GetFileName(entry) is var name && name != ".odysseum" && name != $".{Path.GetFileName(path)}.md"))
            throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be removed. Move their files and subfolders first.");
        var destination = ResolvePath(".odysseum/removed-folders/" + Guid.NewGuid().ToString("N"), true);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Changing(path);
        Directory.Move(path, destination);
        Changed(path);
    }

    /// <summary>Records a path this process is about to change, together with its parent folder, whose
    /// modification time changes with it.</summary>
    private void Changing(string fullPath)
    {
        if (ownWrites is null) return;
        ownWrites.Begin(fullPath);
        if (Path.GetDirectoryName(fullPath) is { } parent) ownWrites.Begin(parent);
    }

    /// <summary>Records the state a path and its parent folder were left in.</summary>
    private void Changed(string fullPath)
    {
        if (ownWrites is null) return;
        ownWrites.Add(fullPath);
        if (Path.GetDirectoryName(fullPath) is { } parent) ownWrites.Add(parent);
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

    private IEnumerable<string> EnumerateDocuments(string directory, bool recursive = true)
    {
        AssertNoLinks(directory);
        if (!Directory.Exists(directory)) yield break;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            var folderDocument = directory != Root && name == $".{Path.GetFileName(directory)}.md";
            if ((name.StartsWith('.') && !folderDocument) || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
            if (Directory.Exists(entry))
            {
                if (!recursive) continue;
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
