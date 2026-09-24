using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/documents")]
public class DocumentsController(IProjectService projects, IDocumentService documents, IFolderService folders, ProjectViews views) : ControllerBase
{
    /// <summary>Every document with its prose, in manuscript order.</summary>
    [HttpGet]
    public async Task<IResult> List(string project) =>
        ApiResults.SuccessCollection(await views.DocumentsAsync(await projects.GetAsync(project)));

    /// <summary>Create a Markdown file with a new writer_id.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateDocumentRequest request)
    {
        var folder = await FolderPaths.EnsureAsync(folders, await projects.GetAsync(project), request.Folder);
        var created = await documents.CreateAsync(folder, request.Title, request.Content);
        return ApiResults.Created(await views.DocumentAsync(created), $"/api/projects/{Uri.EscapeDataString(project)}/documents/{created.Id}");
    }

    /// <summary>Read a document's prose and details from disk.</summary>
    [HttpGet("{id}")]
    public async Task<IResult> Get(string project, string id) =>
        ApiResults.Success(await views.DocumentAsync(await documents.GetAsync(await projects.GetAsync(project), id)));

    /// <summary>Save prose. Returns 409 when the file changed since the given revision.</summary>
    [HttpPut("{id}")]
    public async Task<IResult> Save(string project, string id, [FromBody] SaveDocumentRequest request)
    {
        if (request.Content is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document content is required.");
        var document = await documents.GetAsync(await projects.GetAsync(project), id);
        return ApiResults.Success(await views.DocumentAsync(await documents.SaveBodyAsync(document, request.Content, request.Revision)));
    }

    /// <summary>Update title, synopsis, notes, status, and word goal.</summary>
    [HttpPut("{id}/metadata")]
    public async Task<IResult> UpdateMetadata(string project, string id, [FromBody] MetadataRequest request)
    {
        if (request.Synopsis is null || request.Notes is null) throw new WorkspaceException(WorkspaceError.Invalid, "Synopsis and notes must be strings.");
        if (!Enum.IsDefined(request.Status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        var document = await documents.GetAsync(await projects.GetAsync(project), id);
        var details = new DocumentDetails
        {
            Title = request.Title, Synopsis = request.Synopsis, Notes = request.Notes, Status = (DocumentStatus)request.Status,
            WordGoal = request.WordGoal, Links = request.Links, LinkNotes = request.LinkNotes,
        };
        var updated = await documents.UpdateAsync(document, details, request.Revision);
        return ApiResults.Success(await views.ProjectAsync(updated.Project));
    }

    /// <summary>Move or rename the file while keeping its identity.</summary>
    [HttpPut("{id}/path")]
    public async Task<IResult> Move(string project, string id, [FromBody] MoveDocumentRequest request)
    {
        var (parent, name) = FolderPaths.Split(request.Path);
        if (!DocumentRules.IsDocument(name)) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
        var current = await projects.GetAsync(project);
        var document = await documents.GetAsync(current, id);
        var target = await FolderPaths.EnsureAsync(folders, current, parent);
        var moved = await documents.MoveAsync(document, target, Path.GetFileNameWithoutExtension(name), request.Revision);
        return ApiResults.Success(await views.DocumentAsync(moved));
    }

    /// <summary>List recovery snapshots, newest first.</summary>
    [HttpGet("{id}/snapshots")]
    public async Task<IResult> ListSnapshots(string project, string id)
    {
        var document = await documents.GetAsync(await projects.GetAsync(project), id);
        var snapshots = new List<SnapshotInfo>();
        foreach (var version in await documents.ListVersionsAsync(document))
            snapshots.Add(new(version.Id, version.Created.UtcDateTime, MarkdownDocumentCodec.CountWords(await version.ReadBodyAsync())));
        return ApiResults.SuccessCollection(snapshots);
    }

    /// <summary>Read one snapshot's prose.</summary>
    [HttpGet("{id}/snapshots/{snapshot}")]
    public async Task<IResult> GetSnapshot(string project, string id, string snapshot)
    {
        var document = await documents.GetAsync(await projects.GetAsync(project), id);
        var version = (await documents.ListVersionsAsync(document)).FirstOrDefault(v => v.Id == snapshot)
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "That snapshot no longer exists.");
        return ApiResults.Success(new SnapshotContent(await version.ReadBodyAsync()));
    }
}
