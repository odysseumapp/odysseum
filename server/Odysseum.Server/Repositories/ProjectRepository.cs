using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class ProjectRepository(IStorageContext storage) : CachedRepository<Project>(storage), IProjectRepository
{
    /// <summary>Makes an empty project from the title and word goals of <paramref name="item"/>. The ID and name are new.</summary>
    public async Task<Project> AddAsync(Project item)
    {
        var changes = await Storage.AddProjectAsync(item.Title, item.WordGoal, item.DefaultSceneWordGoal);
        return changes.Projects.Single();
    }

    public async Task<Project> UpdateAsync(Project item, string expectedETag) =>
        ChangedItem(await Storage.UpdateProjectAsync(item, expectedETag), item.Id);

    public Task DeleteAsync(string id, string expectedETag) =>
        throw new WorkspaceException(WorkspaceError.Invalid, "Projects cannot be deleted in Odysseum yet.");

    public Task FindNewProjectsAsync() => Storage.LoadNewProjectsAsync();

    public Task ReloadProjectAsync(string projectId) => Storage.ReloadProjectAsync(projectId);

    protected override string IdOf(Project item) => item.Id;
    protected override string ProjectIdOf(Project item) => item.Id;
    protected override string VersionOf(Project item) => item.ETag;
    protected override IReadOnlyList<Project> ChangedItemsIn(StorageChanges changes) => changes.Projects;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedProjectIds;
}
