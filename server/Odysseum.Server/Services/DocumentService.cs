using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Documents.Events;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Models.Editing;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Projects;
using static Odysseum.Server.Services.Documents.DocumentRules;

namespace Odysseum.Server.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly ProjectSessions _sessions;

    public DocumentService(ProjectSessions sessions)
    {
        _sessions = sessions;
        sessions.DocumentsRemoved += (session, documents) => { foreach (var document in documents) DocumentRemoved?.Invoke(this, new(session.Branch, document)); };
    }

    public event EventHandler<DocumentEventArgs>? DocumentCreated;
    public event EventHandler<DocumentEventArgs>? DocumentSaved;
    public event EventHandler<DocumentEventArgs>? DocumentMoved;
    public event EventHandler<DocumentEventArgs>? DocumentRemoved;

    public Task<IReadOnlyList<IDocument>> ListAsync(IProject project) => Task.FromResult<IReadOnlyList<IDocument>>(Model(project).Documents);

    public Task<IDocument> GetAsync(IProject project, string id) => Task.FromResult<IDocument>(Find(Model(project), id));

    public async Task<IDocument> OpenAsync(ProjectBranch branch, string id)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IDocument>(async () =>
        {
            var document = Find(session.Current, id);
            return document.WithBody(await session.LoadBodyAsync(id));
        });
    }

    public async Task<IReadOnlyList<IDocument>> OpenAllAsync(ProjectBranch branch)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IReadOnlyList<IDocument>>(async () =>
        {
            var documents = new List<IDocument>();
            foreach (var document in session.Current.Walk())
                documents.Add(document.WithBody(await session.LoadBodyAsync(document.Id)));
            return documents;
        });
    }

    public async Task<IDocument> CreateAsync(ProjectBranch branch, string folderId, string title, string? body = null)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IDocument>(async () =>
        {
            var current = session.Current;
            var editor = new DocumentEditor(current);
            var draft = editor.Create(folderId, title, body);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var created = Find(saved, draft.Id).WithBody(await session.LoadBodyAsync(draft.Id));
            DocumentCreated?.Invoke(this, new(branch, created));
            return created;
        });
    }

    public async Task<IDocument> SaveBodyAsync(ProjectBranch branch, string id, string body, string expectedRevision)
    {
        if (body is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document content is required.");
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IDocument>(async () =>
        {
            var current = session.Current;
            var document = Find(current, id);
            ContentRevision.Check(document.Revision, expectedRevision);
            var editor = new DocumentEditor(current);
            editor.SetBody(id, body);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var result = Find(saved, id).WithBody(body);
            DocumentSaved?.Invoke(this, new(branch, result));
            return result;
        });
    }

    public async Task<IDocument> UpdateAsync(ProjectBranch branch, string id, DocumentDetails details, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IDocument>(async () =>
        {
            var current = session.Current;
            ContentRevision.Check(current.Revision, expectedRevision);
            Find(current, id);
            var editor = new DocumentEditor(current);
            editor.SetDetails(id, details);
            var saved = await session.SaveAsync(editor.Changes(), current.Revision);
            var result = Find(saved, id);
            DocumentSaved?.Invoke(this, new(branch, result));
            return result;
        });
    }

    public async Task<IDocument> MoveAsync(ProjectBranch branch, string id, string targetFolderId, string? title, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IDocument>(async () =>
        {
            var current = session.Current;
            var document = Find(current, id);
            ContentRevision.Check(document.Revision, expectedRevision);
            var fileName = title is null ? document.Name : FileName(ValidateTitle(title)) + Path.GetExtension(document.Name);
            var editor = new DocumentEditor(current);
            editor.Move(id, targetFolderId, fileName);
            var changes = editor.Changes();
            var saved = changes.IsEmpty ? current : await session.SaveAsync(changes, current.Revision);
            var result = Find(saved, id);
            DocumentMoved?.Invoke(this, new(branch, result));
            return result;
        });
    }

    private static Document Find(Project project, string id) => project.Document(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.");

    private static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
