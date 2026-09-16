using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Odysseum.Server.Services.WebUi;

/// <summary>Validates UI archives and atomically selects an immutable release directory.</summary>
public sealed class WebUiInstallation(string root)
{
    public const long MaxArchiveBytes = 100 * 1024 * 1024;
    public const long MaxExpandedBytes = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Root { get; } = Path.GetFullPath(root);

    public string? CurrentDirectory => ReadPointer("current.json");
    public string? PreviousDirectory => ReadPointer("previous.json");

    private string? ReadPointer(string filename)
    {
        var path = Path.Combine(Root, filename);
        if (!File.Exists(path)) return null;
        var hash = JsonSerializer.Deserialize<string>(File.ReadAllText(path), Json);
        if (hash is null || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("The installed UI release pointer is invalid.");
        var directory = Path.Combine(Root, "versions", hash);
        return Directory.Exists(directory) ? directory : null;
    }

    public async Task<WebUiRelease> InstallAsync(string archivePath, string? expectedHash = null, string? expectedVersion = null)
    {
        await using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (archiveStream.Length > MaxArchiveBytes) throw new InvalidDataException("The UI archive exceeds 100 MB.");
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archiveStream));
        if (expectedHash is not null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The UI archive checksum does not match the release feed.");
        archiveStream.Position = 0;
        Directory.CreateDirectory(Root);
        await using var installLock = new FileStream(Path.Combine(Root, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var versions = Path.Combine(Root, "versions");
        Directory.CreateDirectory(versions);
        var staging = Path.Combine(versions, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 10000) throw new InvalidDataException("The UI archive contains too many files.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.TrimEnd('/');
                var parts = name.Split('/');
                if (name.Length == 0 || Path.IsPathRooted(name) || name.Contains('\\')
                    || parts.Any(part => string.IsNullOrWhiteSpace(part) || part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(' ')
                        || part.IndexOfAny([':', '<', '>', '"', '|', '?', '*', '\0']) >= 0 || part.Any(char.IsControl))
                    || !paths.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The UI archive contains an unsafe or duplicate path.");
                total = checked(total + entry.Length);
                if (total > MaxExpandedBytes) throw new InvalidDataException("The expanded UI exceeds 256 MB.");
                var destination = Path.GetFullPath(Path.Combine(staging, Path.Combine(parts)));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("The UI archive escapes its installation directory.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
            var release = await ReadReleaseAsync(staging);
            if (expectedVersion is not null && release.Version != expectedVersion)
                throw new InvalidDataException("The archive version does not match the release feed.");
            var installed = Path.Combine(versions, hash);
            if (!Directory.Exists(installed)) Directory.Move(staging, installed);
            else await ReadReleaseAsync(installed);
            // Keep the old pointer until the new release is completely extracted and validated.
            var current = Path.Combine(Root, "current.json");
            if (File.Exists(current)) await AtomicWriteAsync(Path.Combine(Root, "previous.json"), await File.ReadAllTextAsync(current));
            await AtomicWriteAsync(current, JsonSerializer.Serialize(hash, Json));
            return release;
        }
        finally
        {
            // Only this installation's generated staging directory can be removed.
            if (Directory.Exists(staging) && Path.GetDirectoryName(Path.GetFullPath(staging)) == versions
                && Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal)) Directory.Delete(staging, recursive: true);
        }
    }

    public static async Task<WebUiRelease> ReadReleaseAsync(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "index.html"))) throw new InvalidDataException("The UI archive has no index.html at its root.");
        var metadata = Path.Combine(directory, WebUiRelease.ManifestName);
        if (!File.Exists(metadata) || new FileInfo(metadata).Length > 16384) throw new InvalidDataException("The UI release manifest is missing or too large.");
        var release = JsonSerializer.Deserialize<WebUiRelease>(await File.ReadAllTextAsync(metadata), Json)
            ?? throw new InvalidDataException("The UI release manifest is invalid.");
        release.Validate();
        return release;
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
