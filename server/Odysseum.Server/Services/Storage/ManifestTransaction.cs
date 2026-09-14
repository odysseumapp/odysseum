using System.Text.Json;

namespace Odysseum.Server.Services.Storage;

/// <summary>A rollback journal for manifest batches. Only the project lock owner may recover it.</summary>
internal sealed class ManifestTransaction(ProjectFileStore files)
{
    private const string Journal = ".writer/pending-manifests.json";
    internal sealed record Entry(string Path, string? Backup, string After);
    public bool Pending => files.Exists(Journal, metadata: true);

    public async Task RecoverAsync()
    {
        if (!Pending) return;
        Entry[] entries;
        try { entries = JsonSerializer.Deserialize<Entry[]>(await files.ReadAsync(Journal, metadata: true)) ?? throw new JsonException(); }
        catch (JsonException) { throw new WorkspaceException(422, "The manifest recovery journal is invalid."); }
        // Check every file before restoring any, so external edits are never silently overwritten.
        foreach (var entry in entries)
        {
            Validate(entry);
            var current = files.Exists(entry.Path, metadata: true)
                ? ContentRevision.Hash(await files.ReadAsync(entry.Path, metadata: true)) : null;
            var before = entry.Backup is null ? null : ContentRevision.Hash(await files.ReadAsync(entry.Backup, metadata: true));
            if (current != before && current != entry.After)
                throw new WorkspaceException(409, "A manifest changed during recovery. Preserve the pending-manifests journal and resolve the external edit before reopening.");
        }
        foreach (var entry in entries.Reverse())
        {
            if (entry.Backup is null) files.DeleteMetadata(entry.Path);
            else
            {
                var before = await files.ReadAsync(entry.Backup, metadata: true);
                if (!files.Exists(entry.Path, metadata: true)
                    || ContentRevision.Hash(await files.ReadAsync(entry.Path, metadata: true)) != ContentRevision.Hash(before))
                    await files.WriteAsync(entry.Path, before, metadata: true);
            }
        }
        files.DeleteMetadata(Journal);
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
                if (before.ContainsKey(path)) locks.Add(files.LockMetadata(path));
            foreach (var (path, bytes) in before)
            {
                var current = files.Exists(path, metadata: true) ? await files.ReadAsync(path, metadata: true) : null;
                if (current is null || !bytes.AsSpan().SequenceEqual(current))
                    throw new WorkspaceException(409, "Project metadata changed on disk. Refresh before saving again.");
            }
            foreach (var path in changes.Keys.Where(path => !before.ContainsKey(path)))
                if (files.Exists(path, metadata: true)) throw new WorkspaceException(409, "A folder manifest appeared during saving. Refresh and try again.");
            foreach (var (path, bytes) in changes)
            {
                string? backup = null;
                if (before.TryGetValue(path, out var old))
                {
                    backup = $".writer/manifest-transaction/{Guid.NewGuid():N}.bak";
                    await files.WriteAsync(backup, old, overwrite: false, metadata: true);
                }
                entries.Add(new(path, backup, ContentRevision.Hash(bytes)));
            }
            var journal = JsonSerializer.SerializeToUtf8Bytes(entries);
            if (journal.Length > ProjectFileStore.MaxFileBytes) throw new WorkspaceException(413, "Too many manifests in one update.");
            await files.WriteAsync(Journal, journal, overwrite: false, metadata: true);
            // Windows replacement cannot keep a writable handle open on the destination.
            foreach (var handle in locks) handle.Dispose();
            locks.Clear();
            foreach (var (path, bytes) in changes) await files.WriteAsync(path, bytes, metadata: true);
            // Removing the journal is the commit point. Leftover backup files are harmless.
            files.DeleteMetadata(Journal);
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
            try { if (entry.Backup is not null) files.DeleteMetadata(entry.Backup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* An unused backup must not turn a committed save into a failure. */ }
        }
    }

    private static void Validate(Entry entry)
    {
        if (entry is null || entry.Path is null || entry.After is null || entry.After.Length != 64 || !entry.After.All(Uri.IsHexDigit)
            || entry.Path != ".writer/project.json" && (!entry.Path.EndsWith("/.writer/folder.json", StringComparison.Ordinal)
                || entry.Path.Split('/')[..^2].Any(part => part.StartsWith('.')))
            || entry.Backup is not null && (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(entry.Backup), "N", out var id)
                || entry.Backup != $".writer/manifest-transaction/{id:N}.bak"))
            throw new WorkspaceException(422, "The manifest recovery journal is invalid.");
    }
}
