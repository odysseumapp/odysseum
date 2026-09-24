using Odysseum.Server.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.Repositories;

public sealed class ProjectRepository(ProjectLibrary library) : IRepository<Project, string>
{
    public async Task<Project?> GetAsync(string slug) =>
        library.Exists(slug) ? await (await library.OpenProjectAsync(slug)).SnapshotAsync() : null;

    public async Task<IReadOnlyList<Project>> GetAllAsync()
    {
        var projects = new List<Project>();
        foreach (var slug in library.Slugs()) projects.Add(await (await library.OpenProjectAsync(slug)).SnapshotAsync());
        return projects;
    }

    public async Task SaveAsync(Project project)
    {
        var open = project.Open;
        var candidate = open.Current.Manifest.Clone();
        candidate.Settings = project.Settings.Clone();
        await open.CommitAsync(candidate, open.Current.Revision);
    }

    public Task DeleteAsync(string slug) => throw new NotSupportedException("Projects leave the workspace through the file system.");
}
