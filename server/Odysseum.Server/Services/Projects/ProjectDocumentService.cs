using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Storage.Models;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;
using static Odysseum.Server.Services.Storage.ContentRevision;

namespace Odysseum.Server.Services.Projects;

/// <summary>Creates, saves, and moves Markdown documents, including recovery snapshots and revision checks.
/// The coordinator scans before and after these operations while holding the shared project lock.</summary>
internal sealed class ProjectDocumentService(ProjectState state, ProjectFileStore files, DocumentHistoryStore history)
{
    public async Task<string> SaveAsync(string id, SaveDocumentRequest request)
    {
        var document = state.Find(id);
        Check(document.Revision, request.Revision);
        var bytes = Encode(document.Prefix, request.Content);
        if (!bytes.AsSpan().SequenceEqual(document.Bytes))
        {
            await history.SaveAsync(id, document.Bytes);
            // Preserve both the observed disk revision and the attempted draft for recovery.
            await history.SaveAsync(id, bytes);
            Check(Hash(await files.ReadAsync(document.Path)), request.Revision);
            await files.WriteAsync(document.Path, bytes);
        }
        return id;
    }

    public async Task<string> CreateAsync(CreateDocumentRequest request)
    {
        var title = ValidateTitle(request.Title);
        var folder = (request.Folder ?? "").Trim('/');
        var slug = FileName(title);
        var relative = string.IsNullOrEmpty(folder) ? $"{slug}.md" : $"{folder}/{slug}.md";
        var suffix = 2;
        while (files.Exists(relative))
        {
            relative = string.IsNullOrEmpty(folder) ? $"{slug}-{suffix++}.md" : $"{folder}/{slug}-{suffix++}.md";
        }
        var id = Guid.NewGuid().ToString();
        var bytes = Encode($"---\nwriter_id: {id}\n---\n\n", request.Content ?? "");
        await files.WriteAsync(relative, bytes, overwrite: false);
        var candidate = state.Manifest.Clone();
        candidate.Documents[id] = new DocumentMetadata
        {
            Path = relative, Title = title, WordGoal = state.Manifest.Settings.DefaultSceneWordGoal,
            Order = state.Manifest.Documents.Count == 0 ? 0 : state.Manifest.Documents.Values.Max(x => x.Order) + 1
        };
        await state.CommitManifestAsync(candidate, state.Revision);
        return id;
    }

    public async Task<string> MoveAsync(string id, MoveDocumentRequest request)
    {
        var document = state.Find(id);
        Check(document.Revision, request.Revision);
        var relative = request.Path.Replace('\\', '/');
        if (!IsDocument(relative)) throw new WorkspaceException(400, "Use a .md, .markdown, or .txt file name.");
        if (files.Exists(relative) && relative != document.Path) throw new WorkspaceException(409, "A file already exists at that path.");
        if (relative != document.Path)
        {
            Check(Hash(await files.ReadAsync(document.Path)), request.Revision);
            files.Move(document.Path, relative);
            var candidate = state.Manifest.Clone();
            candidate.Documents[id].Path = relative;
            await state.CommitManifestAsync(candidate, state.Revision);
        }
        return id;
    }
}
