using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Loads and saves projects. Callers name a project by its <see cref="ProjectBranch"/> and its items by ID;
/// no paths, streams or storage types cross this interface, so another storage can replace the disk.</summary>
public interface IProjectRepository
{
    /// <summary>Lists the projects without changing them.</summary>
    Task<IReadOnlyList<ProjectInfo>> ListAsync();
    /// <summary>The name of the project with that ID, or null when no project has it.</summary>
    Task<string?> FindNameAsync(string id);
    Task<bool> ExistsAsync(ProjectBranch branch);
    /// <summary>Makes an empty project. Its name comes from the title; a suffix keeps it unique.</summary>
    Task<ProjectInfo> CreateAsync(string title);
    /// <summary>Claims the project for this process until the result is disposed, and repairs an interrupted save.</summary>
    Task<IAsyncDisposable> OpenAsync(ProjectBranch branch);
    /// <summary>The settings, the folder tree with document summaries and the revision. Document bodies are not loaded.</summary>
    Task<ProjectData> LoadAsync(ProjectBranch branch);
    Task<string> LoadBodyAsync(ProjectBranch branch, string documentId);
    /// <summary>Applies the changes when <c>revision</c> is still the stored revision, and returns the state after the save.</summary>
    Task<ProjectData> SaveAsync(ProjectBranch branch, Changes changes, string revision);
}
