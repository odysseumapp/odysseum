using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Repositories.Files;
using System.Text.Json;

namespace Odysseum.Server.Repositories;

internal sealed class ManifestTransaction(IFileManager files)
{
    private const string Journal = ".odysseum/pending-manifests.json";
    internal sealed record Entry(string Path, string? Backup, string After);
    public bool Pending => files.Exists(Journal, metadata: true);

    public async Task RecoverAsync()
    {
        if (!Pending) return;
        Entry[] entries;
        try { entries = JsonSerializer.Deserialize<Entry[]>(await files.ReadAsync(Journal, metadata: true)) ?? throw new JsonException(); }
        catch (JsonException) { throw new WorkspaceException(WorkspaceError.Corrupt, "The manifest recovery journal is invalid."); }
        foreach (var entry in entries)
        {
            Validate(entry);
            var current = files.Exists(entry.Path, metadata: true)
                ? ContentRevision.Hash(await files.ReadAsync(entry.Path, metadata: true)) : null;
            var before = entry.Backup is null ? null : ContentRevision.Hash(await files.ReadAsync(entry.Backup, metadata: true));
            if (current != before && current != entry.After)
                throw new WorkspaceException(WorkspaceError.Conflict, "A manifest changed during recovery. Preserve the pending-manifests journal and resolve the external edit before reopening.");
        }
        foreach (var entry in entries.Reverse())
        {
            if (entry.Backup is null) files.Delete(entry.Path, metadata: true);
            else
            {
                var before = await files.ReadAsync(entry.Backup, metadata: true);
                if (!files.Exists(entry.Path, metadata: true)
                    || ContentRevision.Hash(await files.ReadAsync(entry.Path, metadata: true)) != ContentRevision.Hash(before))
                    await files.WriteAsync(entry.Path, before, metadata: true);
            }
        }
        files.Delete(Journal, metadata: true);
        CleanBackups(entries);
    }

    public async Task CommitAsync(Dictionary<string, byte[]> changes, Dictionary<string, byte[]> before)
    {
        if (changes.Count == 0) return;
        var locks = new List<FileStream>();
        var entries = new List<Entry>();
        try
        {
            foreach (var path in changes.Keys)
                if (before.ContainsKey(path)) locks.Add(files.Lock(path, metadata: true));
            foreach (var (path, bytes) in before)
            {
                var current = files.Exists(path, metadata: true) ? await files.ReadAsync(path, metadata: true) : null;
                if (current is null || !bytes.AsSpan().SequenceEqual(current))
                    throw new WorkspaceException(WorkspaceError.Conflict, "Project metadata changed on disk. Refresh before saving again.");
            }
            foreach (var path in changes.Keys.Where(path => !before.ContainsKey(path)))
                if (files.Exists(path, metadata: true)) throw new WorkspaceException(WorkspaceError.Conflict, "A folder manifest appeared during saving. Refresh and try again.");
            foreach (var (path, bytes) in changes)
            {
                string? backup = null;
                if (before.TryGetValue(path, out var old))
                {
                    backup = $".odysseum/manifest-transaction/{Guid.NewGuid():N}.bak";
                    await files.WriteAsync(backup, old, overwrite: false, metadata: true);
                }
                entries.Add(new(path, backup, ContentRevision.Hash(bytes)));
            }
            var journal = JsonSerializer.SerializeToUtf8Bytes(entries);
            if (journal.Length > FileManager.MaxFileBytes) throw new WorkspaceException(WorkspaceError.TooLarge, "Too many manifests in one update.");
            await files.WriteAsync(Journal, journal, overwrite: false, metadata: true);
            foreach (var handle in locks) handle.Dispose();
            locks.Clear();
            foreach (var (path, bytes) in changes) await files.WriteAsync(path, bytes, metadata: true);
            files.Delete(Journal, metadata: true);
        }
        catch
        {
            foreach (var handle in locks) handle.Dispose();
            locks.Clear();
            await RecoverAsync();
            throw;
        }
        finally
        {
            foreach (var handle in locks) handle.Dispose();
            if (!Pending) CleanBackups(entries);
        }
    }

    private void CleanBackups(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            try { if (entry.Backup is not null) files.Delete(entry.Backup, metadata: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static void Validate(Entry entry)
    {
        if (entry is null || entry.Path is null || entry.After is null || entry.After.Length != 64 || !entry.After.All(Uri.IsHexDigit)
            || entry.Path != ".odysseum/project.json" && (!entry.Path.EndsWith("/.odysseum/folder.json", StringComparison.Ordinal)
                || entry.Path.Split('/')[..^2].Any(part => part.StartsWith('.')))
            || entry.Backup is not null && (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(entry.Backup), "N", out var id)
                || entry.Backup != $".odysseum/manifest-transaction/{id:N}.bak"))
            throw new WorkspaceException(WorkspaceError.Corrupt, "The manifest recovery journal is invalid.");
    }
}
