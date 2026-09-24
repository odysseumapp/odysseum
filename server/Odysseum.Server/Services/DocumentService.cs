using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Documents.Events;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using static Odysseum.Server.Services.Documents.DocumentRules;

namespace Odysseum.Server.Services;

public sealed class DocumentService : IDocumentService
{
    public DocumentService(ProjectLibrary library)
    {
        library.DocumentRemoved += document => DocumentRemoved?.Invoke(this, new(document));
    }

    public event EventHandler<DocumentEventArgs>? DocumentCreated;
    public event EventHandler<DocumentEventArgs>? DocumentSaved;
    public event EventHandler<DocumentEventArgs>? DocumentMoved;
    public event EventHandler<DocumentEventArgs>? DocumentRemoved;

    public Task<IReadOnlyList<IDocument>> ListAsync(IProject project) => Task.FromResult<IReadOnlyList<IDocument>>(Model(project).Documents);

    public Task<IDocument> GetAsync(IProject project, string id) => Task.FromResult<IDocument>(Find(Model(project), id));

    public Task<IDocument> CreateAsync(IFolder folder, string title, string? body = null)
    {
        var validTitle = ValidateTitle(title);
        var open = OpenProject.Of(folder);
        return open.RunAsync<IDocument>(async () =>
        {
            var target = open.Current.Folder(folder.Id) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
            var created = await open.Documents.CreateAsync(target, validTitle, string.IsNullOrEmpty(body) ? null : body);
            DocumentCreated?.Invoke(this, new(created));
            return created;
        });
    }

    public Task<IDocument> SaveBodyAsync(IDocument document, string body, string expectedRevision)
    {
        if (body is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document content is required.");
        var open = OpenProject.Of(document);
        return open.RunAsync<IDocument>(async () =>
        {
            var current = Find(open.Current, document.Id);
            ContentRevision.Check(current.Revision, expectedRevision);
            await open.Documents.SaveAsync(current.WithBody(body));
            var saved = Find(open.Current, document.Id);
            DocumentSaved?.Invoke(this, new(saved));
            return saved;
        });
    }

    public Task<IDocument> UpdateAsync(IDocument document, DocumentDetails details, string expectedRevision)
    {
        var open = OpenProject.Of(document);
        return open.RunAsync<IDocument>(async () =>
        {
            ContentRevision.Check(open.Current.Revision, expectedRevision);
            var current = Find(open.Current, document.Id);
            await open.Documents.SaveAsync(current.With(Validate(open.Current, current, details)));
            var saved = Find(open.Current, document.Id);
            DocumentSaved?.Invoke(this, new(saved));
            return saved;
        });
    }

    public Task<IDocument> MoveAsync(IDocument document, IFolder target, string? title, string expectedRevision)
    {
        var open = OpenProject.Of(document);
        return open.RunAsync<IDocument>(async () =>
        {
            var current = Find(open.Current, document.Id);
            ContentRevision.Check(current.Revision, expectedRevision);
            var destination = open.Current.Folder(target.Id) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
            var fileName = title is null ? current.FileName : FileName(ValidateTitle(title)) + Path.GetExtension(current.FileName);
            var moved = await open.Documents.MoveAsync(current, destination, fileName);
            DocumentMoved?.Invoke(this, new(moved));
            return moved;
        });
    }

    public Task<IReadOnlyList<IDocumentVersion>> ListVersionsAsync(IDocument document)
    {
        var open = OpenProject.Of(document);
        return open.RunAsync<IReadOnlyList<IDocumentVersion>>(async () => await open.Documents.VersionsAsync(Find(open.Current, document.Id)), scan: false);
    }

    private static DocumentDetails Validate(Project project, Document current, DocumentDetails details)
    {
        var title = details.Title is null ? null : ValidateTitle(details.Title);
        if (details.Status is { } status && !Enum.IsDefined(status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        if (details.WordGoal is < 0 or > 10000000) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid word goal.");
        if (details.Synopsis?.Length > 20000 || details.Notes?.Length > 100000) throw new WorkspaceException(WorkspaceError.Invalid, "Notes are too long.");
        IReadOnlyList<string>? links = null;
        if (details.Links is not null)
        {
            if (details.Links.Count > 200) throw new WorkspaceException(WorkspaceError.Invalid, "Too many links attached.");
            links = details.Links.Distinct().ToArray();
            if (links.Any(id => id is null || id == current.Id || project.Document(id) is null))
                throw new WorkspaceException(WorkspaceError.Invalid, "One of the linked documents no longer exists.");
        }
        if (details.LinkNotes is not null && details.LinkNotes.Values.Any(note => note is null || note.Length > 2000))
            throw new WorkspaceException(WorkspaceError.Invalid, "A link note is too long.");
        return details with { Title = title, Links = links };
    }

    private static Document Find(Project project, string id) => project.Document(id)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.");

    private static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
