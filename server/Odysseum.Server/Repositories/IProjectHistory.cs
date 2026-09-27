using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Repositories;

/// <summary>Saved versions and branches of a project. A version is the whole project at one moment.
/// <c>path</c> narrows a call to one document, given as its project-relative path.</summary>
public interface IProjectHistory : IDisposable
{
    /// <summary>Saves a version. A null label means an automatic version, which is skipped when nothing changed.</summary>
    Task<ProjectVersion?> SaveVersionAsync(ProjectBranch branch, string? label);
    Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(ProjectBranch branch, string? path = null, int take = 100);
    /// <summary>Puts the files back as they were in that version, saving the current state first.</summary>
    Task<ProjectVersion> RestoreAsync(ProjectBranch branch, string id, string? path = null);
    /// <summary>The body of one document as it was in that version.</summary>
    Task<string> ReadAsync(ProjectBranch branch, string id, string path);
    Task CreateBranchAsync(ProjectBranch branch, string name);
    Task MergeAsync(ProjectBranch from, ProjectBranch to);
    /// <summary>Releases what the history holds open for that branch.</summary>
    void Close(ProjectBranch branch);
}
