using Odysseum.Server.API.Models;
using Odysseum.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/documents")]
public class DocumentsController : ControllerBase
{
    private readonly ProjectLibrary _library;

    public DocumentsController(ProjectLibrary library)
    {
        _library = library;
    }

    /// <summary>Every document with its prose, in manuscript order.</summary>
    [HttpGet]
    public async Task<IResult> List(string project)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.SuccessCollection(await services.GetAllDocumentsAsync());
    }

    /// <summary>Create a Markdown file with a new writer_id.</summary>
    [HttpPost]
    public async Task<IResult> Create(string project, [FromBody] CreateDocumentRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        var created = await services.CreateAsync(request);
        return ApiResults.Created(created, $"/api/projects/{Uri.EscapeDataString(project)}/documents/{created.Document.Id}");
    }

    /// <summary>Read a document's prose and details from disk.</summary>
    [HttpGet("{id}")]
    public async Task<IResult> Get(string project, string id)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.GetDocumentAsync(id));
    }

    /// <summary>Save prose; rejected with 409 when the file changed since the given revision.</summary>
    [HttpPut("{id}")]
    public async Task<IResult> Save(string project, string id, [FromBody] SaveDocumentRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.SaveAsync(id, request));
    }

    /// <summary>Update title, synopsis, notes, status, and word goal.</summary>
    [HttpPut("{id}/metadata")]
    public async Task<IResult> UpdateMetadata(string project, string id, [FromBody] MetadataRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.UpdateMetadataAsync(id, request));
    }

    /// <summary>Move or rename the file while keeping its identity.</summary>
    [HttpPut("{id}/path")]
    public async Task<IResult> Move(string project, string id, [FromBody] MoveDocumentRequest request)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(await services.MoveAsync(id, request));
    }

    /// <summary>List recovery snapshots, newest first.</summary>
    [HttpGet("{id}/snapshots")]
    public async Task<IResult> ListSnapshots(string project, string id)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.SuccessCollection(await services.GetSnapshotsAsync(id));
    }

    /// <summary>Read one snapshot's prose.</summary>
    [HttpGet("{id}/snapshots/{snapshot}")]
    public async Task<IResult> GetSnapshot(string project, string id, string snapshot)
    {
        var services = await _library.OpenServicesAsync(project);
        return ApiResults.Success(new SnapshotContent(await services.GetSnapshotAsync(id, snapshot)));
    }
}
