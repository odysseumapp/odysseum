using System.Threading.Channels;

namespace Odysseum.Server.Services.Monitoring;

public sealed class ProjectMonitor(ProjectServices services, ILogger logger, int seconds, int versionSeconds = 0) : IAsyncDisposable
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
                && relative != ".odysseum/project.json" && !relative.EndsWith("/.odysseum/folder.json", StringComparison.Ordinal)) return;
            changes.Writer.TryWrite(true);
        }
        watcher.Changed += Changed;
        watcher.Created += Changed;
        watcher.Deleted += Changed;
        watcher.Renamed += (sender, args) => Changed(sender, args);
        watcher.Error += (_, _) => changes.Writer.TryWrite(true);
        try { watcher.EnableRaisingEvents = true; }
        catch (IOException ex) { logger.LogWarning(ex, "File notifications are unavailable for {Root}; using polling.", services.Root); }
        DateTime? changedAt = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var notification = changes.Reader.WaitToReadAsync(iteration.Token).AsTask();
                var wait = TimeSpan.FromSeconds(seconds);
                if (versionSeconds > 0 && changedAt is { } pending)
                {
                    var due = pending.AddSeconds(versionSeconds) - DateTime.UtcNow;
                    if (due < wait) wait = due > TimeSpan.Zero ? due : TimeSpan.Zero;
                }
                var timer = Task.Delay(wait, iteration.Token);
                await Task.WhenAny(notification, timer);
                await iteration.CancelAsync();
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(200, stoppingToken);
                var notified = false;
                while (changes.Reader.TryRead(out _)) notified = true;
                if (notified) changedAt = DateTime.UtcNow;
                try
                {
                    await services.ScanAsync();
                    if (versionSeconds > 0 && changedAt is { } at && DateTime.UtcNow - at >= TimeSpan.FromSeconds(versionSeconds))
                    {
                        changedAt = null;
                        await services.SaveAutomaticVersionAsync();
                    }
                }
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
