namespace Odysseum.Abstractions.History;

/// <summary>Saved versions of a project. A version is the whole project at one moment.</summary>
public interface IHistoryService
{
    Task<ProjectVersion> SaveVersionAsync(string projectId, string name);
    /// <summary>The project's versions, newest first.</summary>
    Task<IReadOnlyList<ProjectVersion>> GetVersionsByProjectIdAsync(string projectId);
    /// <summary>The versions that changed this document, newest first.</summary>
    Task<IReadOnlyList<ProjectVersion>> GetVersionsByDocumentIdAsync(string documentId);
    /// <summary>The document's text as it was in that version.</summary>
    Task<string> GetDocumentTextFromVersionAsync(string documentId, string versionId);
    /// <summary>Puts every file back as it was in that version. The current state is saved first, so a restore can be undone.</summary>
    Task RestoreProjectVersionAsync(string projectId, string versionId);
    /// <summary>Puts one document back as it was in that version. The current state is saved first.</summary>
    Task RestoreDocumentVersionAsync(string documentId, string versionId);
}
