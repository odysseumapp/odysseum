using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class ProjectHandle(string slug, ProjectServices services, ProjectEvents events, ProjectMonitor monitor, IProjectSettingsProvider settings) : IAsyncDisposable
{
    public string Slug { get; } = slug;
    public ProjectServices Services { get; } = services;
    public ProjectEvents Events { get; } = events;
    public IProjectSettingsProvider Settings { get; } = settings;

    public async ValueTask DisposeAsync()
    {
        try { await monitor.DisposeAsync(); }
        finally { Services.Dispose(); }
    }
}
