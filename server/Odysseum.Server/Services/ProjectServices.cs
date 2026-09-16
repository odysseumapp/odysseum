using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

/// <summary>The public entry point for one project's operations. Coordinates focused services under one lock.</summary>
public sealed class ProjectServices : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ProjectFileStore _files;
    private readonly DocumentHistoryStore _history;
    private readonly ProjectState _state;
    private readonly ProjectScanner _scanner;
    private readonly ProjectDocumentService _documents;
    private readonly ProjectOrganizationService _organization;
    private readonly ProjectFolderService _folders;
    private readonly ProjectQueries _queries;
    private FileStream? _instanceLock;
    public string Root => _files.Root;

    public ProjectServices(string root, ProjectEvents events, ISettingsProvider? settings = null)
    {
        _files = new ProjectFileStore(root);
        var manifests = new ProjectManifestStore(_files);
        _history = new DocumentHistoryStore(_files);
        _state = new ProjectState(Root, manifests, events);
        _scanner = new ProjectScanner(_state, _files, manifests);
        _documents = new ProjectDocumentService(_state, _files, _history);
        _organization = new ProjectOrganizationService(_state);
        _folders = new ProjectFolderService(_state, _files, () => settings?.GetSettings().AllowDeletingDefaultFolders ?? false);
        _queries = new ProjectQueries(_state);
    }

    public Task InitializeAsync() => ExecuteAsync(async () =>
    {
        _instanceLock = _files.AcquireInstanceLock();
        await new ManifestTransaction(_files).RecoverAsync();
        await _scanner.ScanAsync();
        return true;
    }, scan: false);

    public Task ScanAsync() => ExecuteAsync(() => Task.FromResult(true));
    public Task<ProjectResponse> GetProjectAsync() => ReadAsync(_queries.GetProject);
    public Task<DocumentContent> GetDocumentAsync(string id) => ReadAsync(() => _queries.GetDocument(id));
    public Task<IReadOnlyList<DocumentContent>> GetAllDocumentsAsync() => ReadAsync(_queries.GetAllDocuments);
    public Task<IProjectSettings> GetSettingsAsync() => ReadAsync(_queries.GetSettings);
    public Task<string> ExportAsync() => ReadAsync(_queries.Export);
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query) => ReadAsync(() => _queries.Search(query));

    public Task<DocumentContent> SaveAsync(string id, SaveDocumentRequest request)
    {
        if (request.Content is null) throw new WorkspaceException(400, "Document content is required.");
        return WriteDocumentAsync(() => _documents.SaveAsync(id, request));
    }

    public Task<DocumentContent> CreateAsync(CreateDocumentRequest request) => WriteDocumentAsync(() => _documents.CreateAsync(request));
    public Task<DocumentContent> MoveAsync(string id, MoveDocumentRequest request) => WriteDocumentAsync(() => _documents.MoveAsync(id, request));

    public Task<ProjectResponse> UpdateMetadataAsync(string id, MetadataRequest request)
    {
        if (request.Synopsis is null || request.Notes is null) throw new WorkspaceException(400, "Synopsis and notes must be strings.");
        return ChangeProjectAsync(() => _organization.UpdateMetadataAsync(id, request));
    }

    public Task<ProjectResponse> UpdateSettingsAsync(IProjectSettings settings, string revision)
    {
        var validated = ProjectSettings.From(settings, out var error);
        if (error is not null) throw new WorkspaceException(400, error);
        return ChangeProjectAsync(() => _organization.UpdateSettingsAsync(validated, revision));
    }

    public Task<ProjectResponse> ReorderAsync(ReorderRequest request) => ChangeProjectAsync(() => _organization.ReorderAsync(request));

    public Task<ProjectResponse> CreateFolderAsync(CreateFolderRequest request) => ChangeFolderAsync(() => _folders.Create(request));
    public Task<ProjectResponse> RemoveFolderAsync(RemoveFolderRequest request) => ChangeFolderAsync(() => _folders.Remove(request));
    public Task<ProjectResponse> SaveFolderLayoutAsync(FolderLayoutRequest request) => ChangeProjectAsync(() => _folders.SaveLayoutAsync(request));

    private Task<ProjectResponse> ChangeFolderAsync(Action change) => ExecuteAsync(async () =>
    {
        change();
        await _scanner.ScanAsync();
        return _queries.GetProject();
    });

    public Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync(string id) => ExecuteAsync(() =>
    {
        _state.Find(id);
        return _history.ListAsync(id);
    }, scan: false);

    public Task<string> GetSnapshotAsync(string id, string snapshot) => ExecuteAsync(() =>
    {
        _state.Find(id);
        return _history.ReadAsync(id, snapshot);
    }, scan: false);

    private Task<T> ReadAsync<T>(Func<T> query) => ExecuteAsync(() => Task.FromResult(query()));

    private Task<DocumentContent> WriteDocumentAsync(Func<Task<string>> write) => ExecuteAsync(async () =>
    {
        var id = await write();
        await _scanner.ScanAsync();
        return _queries.GetDocument(id);
    });

    private Task<ProjectResponse> ChangeProjectAsync(Func<Task> change) => ExecuteAsync(async () =>
    {
        await change();
        return _queries.GetProject();
    });

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, bool scan = true)
    {
        await _gate.WaitAsync();
        try
        {
            if (scan) await _scanner.ScanAsync();
            return await action();
        }
        finally { _gate.Release(); }
    }

    public void Dispose() { _instanceLock?.Dispose(); _gate.Dispose(); }
}
