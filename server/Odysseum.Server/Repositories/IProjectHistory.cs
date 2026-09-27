using Odysseum.Abstractions.History;

namespace Odysseum.Server.Repositories;

/// <summary>Saved versions of a project. A version is the whole project at one moment. A project is named by its folder
/// name in the workspace. <c>path</c> narrows a call to one document, given as its path relative to the project.</summary>
public interface IProjectHistory : IDisposable
{
    /// <summary>Saves a version. A null name means an automatic version, which is skipped when nothing changed.</summary>
    Task<ProjectVersion?> SaveVersionAsync(string projectName, string? name);
    Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(string projectName, string? path = null, int take = 100);
    /// <summary>Puts the files back as they were in that version, saving the current state first.</summary>
    Task<ProjectVersion> RestoreAsync(string projectName, string versionId, string? path = null);
    /// <summary>The text of one document as it was in that version.</summary>
    Task<string> ReadAsync(string projectName, string versionId, string path);
    /// <summary>Releases what the history holds open for that project.</summary>
    void Close(string projectName);
}
