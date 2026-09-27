using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.History;

/// <summary>Saved versions of a project. A version is the whole project at one moment. <c>documentId</c> narrows a
/// call to one document.</summary>
public interface IHistoryService
{
    Task<ProjectVersion> SaveVersionAsync(ProjectBranch branch, string label);
    Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(ProjectBranch branch, string? documentId = null);
    /// <summary>Puts the files back as they were in that version. The current state is saved first, so a restore can
    /// be undone. Returns the project after the restore.</summary>
    Task<IProject> RestoreAsync(ProjectBranch branch, string versionId, string? documentId = null);
    /// <summary>The body of one document as it was in that version.</summary>
    Task<string> ReadAsync(ProjectBranch branch, string versionId, string documentId);
}
