using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Links;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Filters;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class LinksController(IProjectService projects) : ControllerBase
{
    [HttpGet("projects/{projectId}/links")]
    public async Task<IResult> GetLinksByProjectId(string projectId) =>
        ApiResults.SuccessCollection((await projects.GetAllAsync<ILink>(projectId)).Select(LinkDto.FromLink));

    [HttpGet("documents/{documentId}/links")]
    public async Task<IResult> GetLinksByDocumentId(string documentId) =>
        ApiResults.SuccessCollection((await projects.GetLinksForDocumentAsync(documentId)).Select(LinkDto.FromLink));

    [HttpGet("links/{linkId}")]
    public async Task<IResult> GetLinkById(string linkId)
    {
        var link = await projects.GetAsync<ILink>(linkId);
        return ApiResults.Success(LinkDto.FromLink(link), link.ETag);
    }

    /// <summary>Link two documents of the same project. A link has no direction.</summary>
    [HttpPost("links")]
    public async Task<IResult> CreateLink([FromBody] CreateLinkRequest request)
    {
        var link = await projects.CreateLinkAsync(request.FirstDocumentId, request.SecondDocumentId, request.Note ?? "");
        return ApiResults.Created(LinkDto.FromLink(link), $"/api/links/{link.Id}", link.ETag);
    }

    [HttpPut("links/{linkId}/note")]
    [RequireIfMatch]
    public async Task<IResult> UpdateLinkNote(string linkId, [FromBody] UpdateLinkNoteRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        var link = await projects.UpdateLinkNoteAsync(linkId, request.Note, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(LinkDto.FromLink(link), link.ETag);
    }

    [HttpDelete("links/{linkId}")]
    [RequireIfMatch]
    public async Task<IResult> DeleteLink(string linkId, [FromHeader(Name = "If-Match")] string? ifMatch)
    {
        await projects.DeleteLinkAsync(linkId, ETagHeader.Parse(ifMatch));
        return ApiResults.Success(new DeletedItemDto(linkId));
    }
}
