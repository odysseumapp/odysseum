using Microsoft.AspNetCore.Mvc;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class VersionsController(IHistoryService history, IProjectService projects) : ControllerBase
{
    /// <summary>The project's saved versions, newest first.</summary>
    [HttpGet("projects/{projectId}/versions")]
    public async Task<IResult> GetVersionsByProjectId(string projectId) =>
        ApiResults.SuccessCollection((await history.GetVersionsByProjectIdAsync(projectId)).Select(VersionDto.FromVersion));

    /// <summary>Save the project as it is now under a name.</summary>
    [HttpPost("projects/{projectId}/versions")]
    public async Task<IResult> SaveVersion(string projectId, [FromBody] SaveVersionRequest request)
    {
        var version = await history.SaveVersionAsync(projectId, request.Name);
        return ApiResults.Created(VersionDto.FromVersion(version), $"/api/projects/{projectId}/versions/{version.Id}");
    }

    /// <summary>Put every file back as it was in that version. The server saves the current state first, so a restore
    /// can be undone. All folders, documents and links of the project can change; read them again.</summary>
    [HttpPost("projects/{projectId}/versions/{versionId}/restore")]
    public async Task<IResult> RestoreProjectVersion(string projectId, string versionId)
    {
        await history.RestoreProjectVersionAsync(projectId, versionId);
        var project = await projects.GetAsync<IProject>(projectId);
        return ApiResults.Success(ProjectDto.FromProject(project), project.ETag);
    }

    /// <summary>The versions that changed this document, newest first.</summary>
    [HttpGet("documents/{documentId}/versions")]
    public async Task<IResult> GetVersionsByDocumentId(string documentId) =>
        ApiResults.SuccessCollection((await history.GetVersionsByDocumentIdAsync(documentId)).Select(VersionDto.FromVersion));

    /// <summary>The document's text as it was in that version.</summary>
    [HttpGet("documents/{documentId}/versions/{versionId}/text")]
    public async Task<IResult> GetDocumentTextFromVersion(string documentId, string versionId) =>
        ApiResults.Success(new VersionTextDto(versionId, documentId, await history.GetDocumentTextFromVersionAsync(documentId, versionId)));

    /// <summary>Put this document back as it was in that version. The server saves the current state first.</summary>
    [HttpPost("documents/{documentId}/versions/{versionId}/restore")]
    public async Task<IResult> RestoreDocumentVersion(string documentId, string versionId)
    {
        await history.RestoreDocumentVersionAsync(documentId, versionId);
        var document = await projects.GetAsync<IDocument>(documentId);
        return ApiResults.Success(DocumentDto.FromDocument(document), document.ETag);
    }
}
