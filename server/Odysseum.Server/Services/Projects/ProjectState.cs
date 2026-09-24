using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Storage.Models;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services.Projects;

internal sealed class ProjectState
{
    private readonly string _root;
    private readonly ProjectManifestStore _manifests;
    private readonly ProjectEvents _events;
    public ProjectManifest Manifest { get; private set; }
    public string FolderName => Path.GetFileName(_root);
    public IReadOnlyDictionary<string, DiskDocument> Documents { get; private set; } = new Dictionary<string, DiskDocument>();
    public string Revision { get; private set; } = "";
    public string? Warning { get; private set; }

    public ProjectState(string root, ProjectManifestStore manifests, ProjectEvents events)
    {
        _root = root;
        _manifests = manifests;
        _events = events;
        Manifest = NewManifest();
    }

    public ProjectManifest NewManifest() => new() { Settings = new ProjectSettings { Title = Path.GetFileName(_root) } };

    public DiskDocument Find(string id) => Documents.TryGetValue(id, out var document) ? document
        : throw new WorkspaceException(404, "This document was removed or moved outside the workspace. Your browser draft is still available.");

    public async Task CommitManifestAsync(ProjectManifest candidate, string expectedRevision)
    {
        var revision = await _manifests.WriteAsync(candidate, expectedRevision);
        Manifest = candidate;
        Revision = revision;
    }

    public async Task ApplyScanAsync(ProjectManifest candidate, string expectedRevision,
        Dictionary<string, DiskDocument> documents, string? warning)
    {
        var before = Fingerprint();
        await CommitManifestAsync(candidate, expectedRevision);
        Documents = documents;
        Warning = warning;
        if (before != Fingerprint()) PublishChanges();
    }

    public void PublishChanges() => _events.Publish();

    private string Fingerprint() => Revision + "|" + Warning + "|" + string.Join(';',
        Documents.Values.OrderBy(x => x.Id).Select(x => x.Id + x.Path + x.Revision));
}
