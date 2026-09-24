using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Storage.Models;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;
using static Odysseum.Server.Services.Storage.ContentRevision;

namespace Odysseum.Server.Services.Projects;

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
        var bytes = Encode($"---\nwriter_id: {id}\n---\n\n", string.IsNullOrEmpty(request.Content) ? StarterContent(relative) : request.Content);
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

    public async Task<bool> EnsureFolderDocumentsAsync()
    {
        var missing = files.EnumerateFolders().Where(folder => !files.Exists(FolderDocumentPath(folder))).ToArray();
        if (missing.Length == 0) return false;
        var candidate = state.Manifest.Clone();
        var order = candidate.Documents.Count == 0 ? 0 : candidate.Documents.Values.Max(x => x.Order) + 1;
        foreach (var folder in missing)
        {
            var id = Guid.NewGuid().ToString();
            var relative = FolderDocumentPath(folder);
            await files.WriteAsync(relative, Encode($"---\nwriter_id: {id}\n---\n\n", ""), overwrite: false);
            candidate.Documents[id] = new DocumentMetadata { Path = relative, Title = Path.GetFileName(folder), WordGoal = 0, Order = order++ };
        }
        await state.CommitManifestAsync(candidate, state.Revision);
        return true;
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
