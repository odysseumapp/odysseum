using System.Collections.Concurrent;
using System.Threading.Channels;
using Odysseum.Server.Services.Projects;

namespace Odysseum.Server.Repositories.Disk;

/// <summary>Watches each open project folder for changes made by other programs. Paths this process wrote, as recorded
/// in <see cref="OwnWrites"/>, are not reported. Every <c>pollSeconds</c> it reports a change anyway, so a missed
/// notification is caught late instead of never.</summary>
public sealed class FileProjectWatcher(string workspaceRoot, OwnWrites ownWrites, int pollSeconds, ILogger<FileProjectWatcher>? logger = null) : IProjectWatcher
{
    private readonly string _root = Path.GetFullPath(workspaceRoot);
    private readonly OwnWrites _ownWrites = ownWrites;
    private readonly int _pollSeconds = pollSeconds;
    private readonly ILogger<FileProjectWatcher>? _logger = logger;
    private readonly ConcurrentDictionary<string, ProjectWatch> _watches =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public event Action<string>? Changed;

    public void Watch(string projectName)
    {
        _watches.GetOrAdd(projectName, key => new ProjectWatch(this, key, Path.Combine(_root, ProjectNames.Validate(key))));
    }

    public void Unwatch(string projectName)
    {
        if (_watches.TryRemove(projectName, out var watch)) watch.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var projectName in _watches.Keys.ToArray())
            if (_watches.TryRemove(projectName, out var watch)) await watch.StopAsync();
    }

    private sealed class ProjectWatch : IDisposable
    {
        private readonly FileProjectWatcher _owner;
        private readonly string _projectName;
        private readonly string _root;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly Task _loop;

        public ProjectWatch(FileProjectWatcher owner, string projectName, string root)
        {
            _owner = owner;
            _projectName = projectName;
            _root = root;
            _loop = Task.Run(() => RunAsync(_stopping.Token));
        }

        private void OnEvent(string fullPath)
        {
            var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
            if (relative.Split('/').Any(part => part.StartsWith('.'))
                && relative is not (".odysseum/project.json" or ".odysseum/links.json" or ".odysseum/documents.json" or ".odysseum/folders.json") && !relative.EndsWith("/.odysseum/folder.json", StringComparison.Ordinal)) return;
            if (_owner._ownWrites.Contains(fullPath)) return;
            _changes.Writer.TryWrite(true);
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            using var watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, args) => OnEvent(args.FullPath);
            watcher.Created += (_, args) => OnEvent(args.FullPath);
            watcher.Deleted += (_, args) => OnEvent(args.FullPath);
            watcher.Renamed += (_, args) => OnEvent(args.FullPath);
            watcher.Error += (_, _) => _changes.Writer.TryWrite(true);
            try { watcher.EnableRaisingEvents = true; }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            { _owner._logger?.LogWarning(ex, "File notifications are unavailable for {Root}; using polling.", _root); }
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var iteration = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var notification = _changes.Reader.WaitToReadAsync(iteration.Token).AsTask();
                    var timer = Task.Delay(TimeSpan.FromSeconds(_owner._pollSeconds), iteration.Token);
                    await Task.WhenAny(notification, timer);
                    await iteration.CancelAsync();
                    stoppingToken.ThrowIfCancellationRequested();
                    await Task.Delay(200, stoppingToken);
                    while (_changes.Reader.TryRead(out _)) { }
                    _owner.Changed?.Invoke(_projectName);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }

        public async Task StopAsync()
        {
            await _stopping.CancelAsync();
            try { await _loop; } catch (OperationCanceledException) { }
            _stopping.Dispose();
        }

        public void Dispose() => _ = StopAsync();
    }
}
