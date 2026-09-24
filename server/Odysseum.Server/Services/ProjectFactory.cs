using Odysseum.Server.Services.Monitoring;

namespace Odysseum.Server.Services;

public sealed class ProjectFactory(ILoggerFactory loggers, int scanSeconds, int versionSeconds = 0, bool watch = true)
{
    public async Task<ProjectHandle> OpenAsync(string slug, string path)
    {
        var project = new OpenProject(slug, path, new ProjectEvents());
        ProjectMonitor? monitor = null;
        try
        {
            await project.InitializeAsync();
            monitor = new ProjectMonitor(project, loggers.CreateLogger<ProjectMonitor>(), scanSeconds, versionSeconds);
            var handle = new ProjectHandle(slug, project, monitor);
            if (watch) monitor.Start();
            return handle;
        }
        catch
        {
            try { if (monitor is not null) await monitor.DisposeAsync(); }
            finally { project.Dispose(); }
            throw;
        }
    }
}
