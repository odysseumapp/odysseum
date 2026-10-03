using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Filters;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class DocumentsController(IProjectService projects) : ControllerBase
{
    /// <summary>Every document of the project in manuscript order, without the text.</summary>
    [HttpGet("projects/{projectId}/documents")]
    public async Task<IResult> GetDocumentsByProjectId(string projectId) =>
        ApiResults.SuccessCollection((await projects.GetDocumentsInOrderAsync(projectId)).Select(DocumentDto.FromDocument));

    /// <summary>The documents among the folder's children, in order, without the text.</summary>
    [HttpGet("folders/{folderId}/documents")]
    public async Task<IResult> GetDocumentsByFolderId(string folderId) =>
        ApiResults.SuccessCollection((await projects.GetChildrenAsync(folderId)).Select(DocumentDto.FromDocument));

    [HttpGet("documents/{documentId}")]
    public async Task<IResult> GetDocumentById(string documentId)
    {
        var document = await projects.GetAsync<IDocument>(documentId);
        return ApiResults.Success(DocumentDto.FromDocument(document), document.ETag);
    }

    [HttpGet("documents/{documentId}/text")]
    public async Task<IResult> GetDocumentText(string documentId)
    {
        var document = await projects.GetAsync<IDocument>(documentId);
        var text = await projects.GetDocumentTextAsync(documentId);
        return ApiResults.Success(new DocumentTextDto(document.Id, text, document.ETag), document.ETag);
    }

    /// <summary>Make a document at the end of the folder's children. The file name comes from the title.</summary>
    [HttpPost("documents")]
    public async Task<IResult> CreateDocument([FromBody] CreateDocumentRequest request)
    {
        var document = await projects.CreateDocumentAsync(request.FolderId, request.Title, request.Text);
        return ApiResults.Created(DocumentDto.FromDocument(document), $"/api/documents/{document.Id}", document.ETag);
    }

    [HttpPut("documents/{documentId}/text")]
    [RequireIfMatch]
    public async Task<IResult> UpdateDocumentText(string documentId, [FromBody] UpdateDocumentTextRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var document = await projects.UpdateDocumentTextAsync(documentId, request.Text, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(DocumentDto.FromDocument(document), document.ETag);
    }

    /// <summary>Replace the title, synopsis, notes, status and word goal.</summary>
    [HttpPut("documents/{documentId}/details")]
    [RequireIfMatch]
    public async Task<IResult> UpdateDocumentDetails(string documentId, [FromBody] UpdateDocumentDetailsRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var details = new DocumentDetails(request.Title, request.Synopsis, request.Notes, request.Status!.Value, request.WordGoal!.Value);
        var document = await projects.UpdateDocumentDetailsAsync(documentId, details, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(DocumentDto.FromDocument(document), document.ETag);
    }

    /// <summary>Change the document's file name. The document keeps its ID, details and links.</summary>
    [HttpPut("documents/{documentId}/name")]
    [RequireIfMatch]
    public async Task<IResult> RenameDocument(string documentId, [FromBody] RenameDocumentRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var document = await projects.RenameDocumentAsync(documentId, request.Name, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(DocumentDto.FromDocument(document), document.ETag);
    }

    /// <summary>Move the document into another folder, or to another place in the same folder.</summary>
    [HttpPut("documents/{documentId}/folder")]
    [RequireIfMatch]
    public async Task<IResult> MoveDocumentToFolder(string documentId, [FromBody] MoveDocumentRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var result = await projects.MoveDocumentToFolderAsync(documentId, request.TargetFolderId, request.Index!.Value, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(DocumentMoveDto.FromMoveResult(result), result.Document.ETag);
    }
}
