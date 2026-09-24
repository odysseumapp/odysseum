using System.Text;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Projects;
using static Odysseum.Server.Repositories.ContentRevision;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Repositories;

public sealed class DocumentRepository(OpenProject project) : IRepository<Document, string>
{
    public Task<Document?> GetAsync(string id) => Task.FromResult(project.Current.Document(id));

    public Task<IReadOnlyList<Document>> GetAllAsync() => Task.FromResult(project.Current.Documents);

    public Task DeleteAsync(string id) => throw new NotSupportedException("Documents leave a project through the file system.");

    public async Task SaveAsync(Document document)
    {
        var current = Find(document.Id);
        var bytes = Encode(current.Disk.Prefix, document.Body);
        var bodyChanged = !bytes.AsSpan().SequenceEqual(current.Disk.Bytes);
        if (bodyChanged)
        {
            await project.History.SaveAsync(current.Id, current.Disk.Bytes);
            await project.History.SaveAsync(current.Id, bytes);
            Check(Hash(await project.Files.ReadAsync(current.Path)), current.Revision);
            await project.Files.WriteAsync(current.Path, bytes);
        }
        if (DetailsChanged(document, current))
        {
            var candidate = project.Current.Manifest.Clone();
            var metadata = candidate.Documents[current.Id];
            metadata.Title = document.Title;
            metadata.Synopsis = document.Synopsis;
            metadata.Notes = document.Notes;
            metadata.Status = document.Status;
            metadata.WordGoal = document.WordGoal;
            var links = document.LinkIds.Distinct().ToList();
            var before = Links.Of(candidate, current.Id);
            foreach (var removed in before.Except(links))
            {
                candidate.Documents[removed].Links.Remove(current.Id);
                candidate.Documents[removed].LinkNotes.Remove(current.Id);
                metadata.LinkNotes.Remove(removed);
            }
            foreach (var added in links.Except(before))
                if (!candidate.Documents[added].Links.Contains(current.Id)) candidate.Documents[added].Links.Add(current.Id);
            metadata.Links = links;
            foreach (var other in links)
            {
                var note = document.LinkNotes.GetValueOrDefault(other)?.Trim() ?? "";
                if (note.Length == 0)
                {
                    metadata.LinkNotes.Remove(other);
                    candidate.Documents[other].LinkNotes.Remove(current.Id);
                }
                else metadata.LinkNotes[other] = candidate.Documents[other].LinkNotes[current.Id] = note;
            }
            await project.CommitAsync(candidate, project.Current.Revision);
        }
        if (bodyChanged) await project.RescanAsync();
    }

    private static bool DetailsChanged(Document document, Document current) =>
        document.Title != current.Title || document.Synopsis != current.Synopsis || document.Notes != current.Notes
        || document.Status != current.Status || document.WordGoal != current.WordGoal
        || !document.LinkIds.ToHashSet(StringComparer.Ordinal).SetEquals(current.LinkIds)
        || document.LinkNotes.Count != current.LinkNotes.Count
        || document.LinkNotes.Any(pair => current.LinkNotes.GetValueOrDefault(pair.Key) != pair.Value);

    public async Task<Document> CreateAsync(Folder folder, string title, string? body, string? fileName = null)
    {
        var name = fileName ?? FileName(title) + ".md";
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var relative = folder.ChildPath(name);
        var suffix = 2;
        while (project.Files.Exists(relative)) relative = folder.ChildPath($"{stem}-{suffix++}{extension}");
        var id = Guid.NewGuid().ToString();
        var bytes = Encode($"---\nwriter_id: {id}\n---\n\n", body ?? StarterContent(relative));
        await project.Files.WriteAsync(relative, bytes, overwrite: false);
        var candidate = project.Current.Manifest.Clone();
        candidate.Documents[id] = new DocumentMetadata
        {
            Path = relative, Title = title,
            WordGoal = IsFolderDocument(relative) ? 0 : candidate.Settings.DefaultSceneWordGoal,
        };
        OrderService.Append(Owner(candidate, folder), id);
        await project.CommitAsync(candidate, project.Current.Revision);
        await project.RescanAsync();
        return Find(id);
    }

    public async Task<Document> MoveAsync(Document document, Folder target, string fileName)
    {
        var current = Find(document.Id);
        var destination = project.Current.Folder(target.Id) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
        var relative = destination.ChildPath(fileName);
        if (!IsDocument(relative)) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
        if (relative == current.Path) return current;
        if (project.Files.Exists(relative)) throw new WorkspaceException(WorkspaceError.Conflict, "A file already exists at that path.");
        Check(Hash(await project.Files.ReadAsync(current.Path)), current.Revision);
        project.Files.Move(current.Path, relative);
        var candidate = project.Current.Manifest.Clone();
        candidate.Documents[current.Id].Path = relative;
        if (destination.Id != current.Location.Id)
        {
            OrderService.Remove(Owner(candidate, current.Location), current.Id);
            OrderService.Append(Owner(candidate, destination), current.Id);
        }
        await project.CommitAsync(candidate, project.Current.Revision);
        await project.RescanAsync();
        return Find(current.Id);
    }

    public Task<IReadOnlyList<DocumentVersion>> VersionsAsync(Document document) => project.History.ListAsync(Find(document.Id));

    public async Task<bool> EnsureFolderDocumentsAsync()
    {
        var missing = project.Files.EnumerateFolders().Where(folder => !project.Files.Exists(FolderDocumentPath(folder))).ToArray();
        if (missing.Length == 0) return false;
        var candidate = project.Current.Manifest.Clone();
        foreach (var folder in missing)
        {
            var id = Guid.NewGuid().ToString();
            var relative = FolderDocumentPath(folder);
            await project.Files.WriteAsync(relative, Encode($"---\nwriter_id: {id}\n---\n\n", ""), overwrite: false);
            candidate.Documents[id] = new DocumentMetadata { Path = relative, Title = Path.GetFileName(folder), WordGoal = 0 };
        }
        await project.CommitAsync(candidate, project.Current.Revision);
        return true;
    }

    public async Task<Project> ScanAsync()
    {
        var current = project.Current;
        var persisted = await project.Manifests.ReadAsync();
        var candidate = persisted.Revision == current.Revision ? current.Manifest.Clone() : persisted.Manifest ?? project.NewManifest();
        var next = new Dictionary<string, DiskDocument>();
        var warnings = new List<string>();
        var knownPaths = candidate.Documents.ToDictionary(x => x.Key, x => x.Value.Path);
        var paths = project.Files.EnumerateDocuments().Order(StringComparer.Ordinal).ToArray();
        var livePaths = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var relativePath in paths)
        {
            byte[] bytes;
            try { bytes = await project.Files.ReadAsync(relativePath); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { throw new WorkspaceException(WorkspaceError.Unavailable, "A document is still being written. Odysseum will retry shortly."); }
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
                throw new WorkspaceException(WorkspaceError.Conflict, $"Two files have the same writer_id. Give the copy a new ID: {relativePath}");
            next[id] = new(id, relativePath, bytes, prefix, body, hash, project.Files.LastModified(relativePath));
            if (!candidate.Documents.TryGetValue(id, out var metadata))
            {
                metadata = new DocumentMetadata
                {
                    Title = IsFolderDocument(relativePath) ? Path.GetFileName(Path.GetDirectoryName(relativePath)!) : Path.GetFileNameWithoutExtension(relativePath),
                    WordGoal = IsFolderDocument(relativePath) ? 0 : candidate.Settings.DefaultSceneWordGoal,
                };
                candidate.Documents[id] = metadata;
            }
            metadata.Path = relativePath;
            metadata.LastKnownHash = hash;
        }
        var revision = await project.Manifests.WriteAsync(candidate, persisted.Revision);
        return new Project(project, candidate, revision, next, warnings.Count > 0 ? string.Join(" ", warnings) : null);
    }

    private static FolderManifest Owner(ProjectManifest candidate, Folder folder) => folder.IsRoot ? candidate
        : candidate.FolderManifests.GetValueOrDefault(folder.Path) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");

    private Document Find(string id) => project.Current.Document(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.");
}
