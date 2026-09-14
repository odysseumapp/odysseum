using System.Threading.Channels;

namespace Odysseum.Server.Services.Monitoring;

/// <summary>Watches one project directory and rescans its services after filesystem notifications or on a timer.</summary>
public sealed class ProjectMonitor(ProjectServices services, ILogger logger, int seconds) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task _loop = Task.CompletedTask;

    public void Start() => _loop = Task.Run(() => RunAsync(_stopping.Token));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        using var watcher = new FileSystemWatcher(services.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        void Changed(object? sender, FileSystemEventArgs args)
        {
            var relative = Path.GetRelativePath(services.Root, args.FullPath).Replace('\\', '/');
            if (relative.Split('/').Any(part => part.StartsWith('.'))
                && relative != ".writer/project.json" && !relative.EndsWith("/.writer/folder.json", StringComparison.Ordinal)) return;
            changes.Writer.TryWrite(true);
        }
        watcher.Changed += Changed;
        watcher.Created += Changed;
        watcher.Deleted += Changed;
        watcher.Renamed += (sender, args) => Changed(sender, args);
        watcher.Error += (_, _) => changes.Writer.TryWrite(true);
        try { watcher.EnableRaisingEvents = true; }
        catch (IOException ex) { logger.LogWarning(ex, "File notifications are unavailable for {Root}; using polling.", services.Root); }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var notification = changes.Reader.WaitToReadAsync(iteration.Token).AsTask();
                var timer = Task.Delay(TimeSpan.FromSeconds(seconds), iteration.Token);
                await Task.WhenAny(notification, timer);
                await iteration.CancelAsync();
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(200, stoppingToken);
                while (changes.Reader.TryRead(out _)) { }
                try { await services.ScanAsync(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException)
                { logger.LogWarning("Scan of {Root} deferred: {Message}", services.Root, ex.Message); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try { await _loop; } catch (OperationCanceledException) { }
        _stopping.Dispose();
    }
}
