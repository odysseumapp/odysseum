using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services.Monitoring;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Services;

public sealed class OpenProject : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _instanceLock;
    private IProjectVersionRepository? _versions;

    public OpenProject(string slug, string root, ProjectEvents events)
    {
        Slug = slug;
        Files = new FileManager(root);
        Manifests = new ProjectManifestRepository(Files);
        History = new DocumentVersionRepository(Files);
        Events = events;
        Documents = new DocumentRepository(this);
        Folders = new FolderRepository(this);
        Current = new Project(this, NewManifest(), "", new Dictionary<string, DiskDocument>(), null);
    }

    public string Slug { get; }
    public string Root => Files.Root;
    public IFileManager Files { get; }
    public IProjectManifestRepository Manifests { get; }
    public IDocumentVersionRepository History { get; }
    public ProjectEvents Events { get; }
    public DocumentRepository Documents { get; }
    public FolderRepository Folders { get; }
    public Project Current { get; private set; }
    public event Action<Project>? Changed;
    public event Action<IReadOnlyList<Document>>? DocumentsRemoved;
    public event Action<IReadOnlyList<Folder>>? FoldersRemoved;
    public IProjectVersionRepository Versions => _versions ?? throw new InvalidOperationException("The project has not been initialized.");

    internal ProjectManifest NewManifest() => new() { Settings = new ProjectSettings { Title = Slug } };

    public Task InitializeAsync() => RunAsync(async () =>
    {
        _instanceLock = Files.AcquireInstanceLock();
        await new ManifestTransaction(Files).RecoverAsync();
        _versions = new ProjectVersionRepository(Root);
        await RescanAsync();
        if (Current.Documents.Count > 0) Versions.Save(null);
        return true;
    }, scan: false);

    public Task<Project> SnapshotAsync() => RunAsync(() => Task.FromResult(Current));

    public Task ScanAsync() => SnapshotAsync();

    public async Task<T> RunAsync<T>(Func<Task<T>> action, bool scan = true)
    {
        await _gate.WaitAsync();
        try
        {
            if (scan) await RescanAsync();
            return await action();
        }
        finally { _gate.Release(); }
    }

    internal async Task RescanAsync()
    {
        Apply(await Documents.ScanAsync());
        if (await Documents.EnsureFolderDocumentsAsync()) Apply(await Documents.ScanAsync());
    }

    internal async Task<Project> CommitAsync(ProjectManifest candidate, string expectedRevision)
    {
        var revision = await Manifests.WriteAsync(candidate, expectedRevision);
        Apply(new Project(this, candidate, revision, Current.Disk, Current.Warning));
        return Current;
    }

    internal void Apply(Project next)
    {
        var previous = Current;
        var changed = previous.Fingerprint() != next.Fingerprint();
        Current = next;
        if (!changed) return;
        Events.Publish();
        Changed?.Invoke(next);
        var documents = previous.Documents.Where(document => next.Document(document.Id) is null).ToArray();
        if (documents.Length > 0) DocumentsRemoved?.Invoke(documents);
        var folders = previous.Folders.Where(folder => next.Folder(folder.Id) is null).ToArray();
        if (folders.Length > 0) FoldersRemoved?.Invoke(folders);
    }

    public Task<IReadOnlyList<VersionInfo>> ListVersionsAsync() => RunAsync(() => Task.FromResult(Versions.List()), scan: false);

    public Task<VersionInfo> SaveVersionAsync(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 200 || name.Contains('\n'))
            throw new WorkspaceException(WorkspaceError.Invalid, "A version name is one line of up to 200 characters.");
        return RunAsync(() => Task.FromResult(Versions.Save(name)!));
    }

    public Task<VersionInfo?> SaveAutomaticVersionAsync() => RunAsync(() => Task.FromResult(Versions.Save(null)));

    public Task<Project> RestoreVersionAsync(string id) => RunAsync(async () =>
    {
        Versions.Restore(id);
        await RescanAsync();
        return Current;
    });

    public static OpenProject Of(IProject project) => (project as Project)?.Open
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
    public static OpenProject Of(IFolder folder) => Of(folder.Project);
    public static OpenProject Of(IDocument document) => Of(document.Project);

    public void Dispose()
    {
        _versions?.Dispose();
        _instanceLock?.Dispose();
        _gate.Dispose();
    }
}
