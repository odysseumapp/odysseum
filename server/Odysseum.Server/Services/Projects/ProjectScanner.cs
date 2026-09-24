using System.Text;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Storage.Models;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;
using static Odysseum.Server.Services.Storage.ContentRevision;

namespace Odysseum.Server.Services.Projects;

internal sealed class ProjectScanner(ProjectState state, ProjectFileStore files, ProjectManifestStore manifests)
{
    public async Task ScanAsync()
    {
        var persisted = await manifests.ReadAsync();
        var candidate = persisted.Revision == state.Revision ? state.Manifest.Clone() : persisted.Manifest ?? state.NewManifest();
        var next = new Dictionary<string, DiskDocument>();
        var warnings = new List<string>();
        var knownPaths = candidate.Documents.ToDictionary(x => x.Key, x => x.Value.Path);
        var paths = files.EnumerateDocuments().Order(StringComparer.Ordinal).ToArray();
        var livePaths = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var relativePath in paths)
        {
            byte[] bytes;
            try { bytes = await files.ReadAsync(relativePath); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { throw new WorkspaceException(503, "A document is still being written. Odysseum will retry shortly."); }
            catch (WorkspaceException ex) { warnings.Add($"{relativePath}: {ex.Message}"); continue; }
            string text;
            try { text = Decode(bytes); }
            catch (DecoderFallbackException) { warnings.Add($"{relativePath} is not UTF-8 and was left untouched."); continue; }
            var (prefix, body, embeddedId) = Split(text);
            var hash = Hash(bytes);
            var id = embeddedId ?? knownPaths.FirstOrDefault(x => x.Value == relativePath).Key;
            if (id is null)
            {
                var matches = candidate.Documents.Where(x => !livePaths.Contains(x.Value.Path)
                    && x.Value.LastKnownHash == hash && !next.ContainsKey(x.Key)).ToArray();
                if (matches.Length == 1 && paths.Count(p => p != relativePath && !knownPaths.ContainsValue(p)) == 0)
                    id = matches[0].Key;
            }
            id ??= Guid.NewGuid().ToString();
            if (next.ContainsKey(id))
                throw new WorkspaceException(409, $"Two files have the same writer_id. Give the copy a new ID: {relativePath}");
            next[id] = new(id, relativePath, bytes, prefix, body, hash, files.LastModified(relativePath));
            if (!candidate.Documents.TryGetValue(id, out var metadata))
            {
                metadata = new DocumentMetadata
                {
                    Title = IsFolderDocument(relativePath) ? Path.GetFileName(Path.GetDirectoryName(relativePath)!) : Path.GetFileNameWithoutExtension(relativePath),
                    WordGoal = IsFolderDocument(relativePath) ? 0 : candidate.Settings.DefaultSceneWordGoal,
                    Order = candidate.Documents.Count == 0 ? 0 : candidate.Documents.Values.Max(x => x.Order) + 1
                };
                candidate.Documents[id] = metadata;
            }
            metadata.Path = relativePath;
            metadata.LastKnownHash = hash;
        }
        await state.ApplyScanAsync(candidate, persisted.Revision, next,
            warnings.Count > 0 ? string.Join(" ", warnings) : null);
    }
}
