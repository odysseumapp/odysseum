using Odysseum.Server.Services.Monitoring;

namespace Odysseum.Server.Services;

public sealed class ProjectHandle(string slug, OpenProject project, ProjectMonitor monitor) : IAsyncDisposable
{
    public string Slug { get; } = slug;
    public OpenProject Project { get; } = project;
    public ProjectEvents Events => Project.Events;

    public async ValueTask DisposeAsync()
    {
        try { await monitor.DisposeAsync(); }
        finally { Project.Dispose(); }
    }
}
