using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

/// <summary>Constructs and starts one project. The library owns the returned handle until shutdown.</summary>
public sealed class ProjectFactory(ILoggerFactory loggers, int scanSeconds, ISettingsProvider? settings = null, int versionSeconds = 0)
{
    public async Task<ProjectHandle> OpenAsync(string slug, string path)
    {
        var events = new ProjectEvents();
        var services = new ProjectServices(path, events, settings);
        ProjectMonitor? monitor = null;
        try
        {
            await services.InitializeAsync();
            monitor = new ProjectMonitor(services, loggers.CreateLogger<ProjectMonitor>(), scanSeconds, versionSeconds);
            var settings = new ProjectSettingsProvider(slug, services, loggers.CreateLogger<ProjectSettingsProvider>());
            var handle = new ProjectHandle(slug, services, events, monitor, settings);
            monitor.Start();
            return handle;
        }
        catch
        {
            try { if (monitor is not null) await monitor.DisposeAsync(); }
            finally { services.Dispose(); }
            throw;
        }
    }
}
