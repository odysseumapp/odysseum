using System.Collections.Concurrent;

namespace Odysseum.Server.Repositories.Disk;

/// <summary>The paths this process changed recently, with the state each one was left in. The file watcher asks it so
/// that the server's own writes are not reported as external changes. A path whose state differs from the recorded one
/// (an edit from outside right after a save) is reported. Entries expire after a few seconds.</summary>
public sealed class OwnWrites
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private sealed record Entry(DateTime At, string? State);

    /// <summary>Records that this process is about to change the path. Until <see cref="Add"/> follows, any event for
    /// the path counts as our own.</summary>
    public void Begin(string fullPath) => Put(fullPath, null);

    /// <summary>Records the state the path was left in after our change.</summary>
    public void Add(string fullPath) => Put(fullPath, Snapshot(fullPath));

    public bool Contains(string fullPath)
    {
        if (!_entries.TryGetValue(Normalize(fullPath), out var entry) || DateTime.UtcNow - entry.At > Lifetime) return false;
        return entry.State is null || entry.State == Snapshot(fullPath);
    }

    private void Put(string fullPath, string? state)
    {
        var now = DateTime.UtcNow;
        _entries[Normalize(fullPath)] = new Entry(now, state);
        if (_entries.Count > 256)
            foreach (var (key, entry) in _entries) if (now - entry.At > Lifetime) _entries.TryRemove(key, out _);
    }

    private static string Snapshot(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            if (file.Exists) return $"file:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            return Directory.Exists(fullPath) ? "folder" : "none";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "unknown"; }
    }

    private static string Normalize(string fullPath) => Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
