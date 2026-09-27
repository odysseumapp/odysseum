using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.API.Views;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/documents")]
public class DocumentsController(IProjectService projects, IDocumentService documents, IFolderService folders, IHistoryService history, ProjectViews views) : ControllerBase
{
    /// <summary>Every document with its prose, in manuscript order.</summary>
    [HttpGet]
    public async Task<IResult> List(string project) =>
        ApiResults.SuccessCollection(await views.DocumentsAsync(ProjectViews.Model(await projects.GetAsync(project))));

    /// <summary>Create a Markdown file with a new writer_id.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateDocumentRequest request)
    {
        var current = await projects.GetAsync(project);
        var folder = await FolderPaths.EnsureAsync(folders, current, request.Folder);
        var created = await documents.CreateAsync(current.Branch, folder.Id, request.Title, request.Content);
        var after = ProjectViews.Model(await projects.GetAsync(current.Branch));
        return ApiResults.Created(await views.ViewAsync(after, created), $"/api/projects/{Uri.EscapeDataString(project)}/documents/{created.Id}");
    }

    /// <summary>Read a document's prose and details from disk.</summary>
    [HttpGet("{id}")]
    public async Task<IResult> Get(string project, string id)
    {
        var current = ProjectViews.Model(await projects.GetAsync(project));
        return ApiResults.Success(await views.ViewAsync(current, await documents.OpenAsync(current.Branch, id)));
    }

    /// <summary>Save prose. Returns 409 when the file changed since the given revision.</summary>
    [HttpPut("{id}")]
    public async Task<IResult> Save(string project, string id, [FromBody] SaveDocumentRequest request)
    {
        if (request.Content is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document content is required.");
        var current = await projects.GetAsync(project);
        var saved = await documents.SaveBodyAsync(current.Branch, id, request.Content, request.Revision);
        return ApiResults.Success(await views.ViewAsync(ProjectViews.Model(await projects.GetAsync(current.Branch)), saved));
    }

    /// <summary>Update title, synopsis, notes, status, and word goal.</summary>
    [HttpPut("{id}/metadata")]
    public async Task<IResult> UpdateMetadata(string project, string id, [FromBody] MetadataRequest request)
    {
        if (request.Synopsis is null || request.Notes is null) throw new WorkspaceException(WorkspaceError.Invalid, "Synopsis and notes must be strings.");
        if (!Enum.IsDefined(request.Status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        var current = await projects.GetAsync(project);
        var details = new DocumentDetails
        {
            Title = request.Title, Synopsis = request.Synopsis, Notes = request.Notes, Status = (DocumentStatus)request.Status,
            WordGoal = request.WordGoal, Links = request.Links, LinkNotes = request.LinkNotes,
        };
        await documents.UpdateAsync(current.Branch, id, details, request.Revision);
        return ApiResults.Success(views.View(ProjectViews.Model(await projects.GetAsync(current.Branch))));
    }

    /// <summary>Move or rename the file while keeping its identity.</summary>
    [HttpPut("{id}/path")]
    public async Task<IResult> Move(string project, string id, [FromBody] MoveDocumentRequest request)
    {
        var (parent, name) = FolderPaths.Split(request.Path);
        if (!DocumentRules.IsDocument(name)) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
        var current = await projects.GetAsync(project);
        var target = await FolderPaths.EnsureAsync(folders, current, parent);
        var moved = await documents.MoveAsync(current.Branch, id, target.Id, Path.GetFileNameWithoutExtension(name), request.Revision);
        return ApiResults.Success(await views.ViewAsync(ProjectViews.Model(await projects.GetAsync(current.Branch)), moved));
    }

    /// <summary>The saved versions that changed this document, newest first.</summary>
    [HttpGet("{id}/versions")]
    public async Task<IResult> ListVersions(string project, string id)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.SuccessCollection((await history.ListVersionsAsync(current.Branch, id)).Select(VersionsController.Describe));
    }

    /// <summary>The document's prose as it was in one version.</summary>
    [HttpGet("{id}/versions/{version}")]
    public async Task<IResult> GetVersion(string project, string id, string version)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.Success(new VersionContent(await history.ReadAsync(current.Branch, version, id)));
    }

    /// <summary>Put this document back as it was in that version. The server saves the current state first.</summary>
    [HttpPost("{id}/versions/{version}/restore")]
    public async Task<IResult> RestoreVersion(string project, string id, string version)
    {
        var current = await projects.GetAsync(project);
        return ApiResults.Success(views.View(ProjectViews.Model(await history.RestoreAsync(current.Branch, version, id))));
    }
}
