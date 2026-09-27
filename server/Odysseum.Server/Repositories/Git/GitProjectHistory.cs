using System.Text.RegularExpressions;
using LibGit2Sharp;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Projects;

namespace Odysseum.Server.Repositories.Git;

/// <summary>Project versions as commits in a git repository at <c>&lt;workspace&gt;/&lt;project&gt;/.git</c>.
/// Only the <c>main</c> branch exists; branch and merge operations are not supported yet.</summary>
public sealed partial class GitProjectHistory(string workspaceRoot) : IProjectHistory
{
    private const string AutomaticMessage = "Automatic version";
    private const string BeforeRestoreMessage = "Before restoring";
    private const string RestoredPrefix = "Restored the version from ";
    private static readonly string[] Excluded =
    [
        "/.odysseum/instance.lock", "/.odysseum/pending-manifests.json",
        "/.odysseum/manifest-transaction/", "/.odysseum/removed-folders/", ".odysseum-*.tmp",
    ];
    private readonly string _root = Path.GetFullPath(workspaceRoot);
    private readonly Dictionary<string, Repository> _repositories =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    static GitProjectHistory()
    {
        GlobalSettings.SetOwnerValidation(false);
    }

    public Task<ProjectVersion?> SaveVersionAsync(ProjectBranch branch, string? label) =>
        Task.FromResult(Guarded(branch, repository => SaveIfChanged(repository, label, AutomaticMessage)));

    public Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(ProjectBranch branch, string? path = null, int take = 100) =>
        Task.FromResult(Guarded<IReadOnlyList<ProjectVersion>>(branch, repository =>
        {
            if (repository.Head.Tip is null) return [];
            var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological };
            var commits = path is null ? repository.Commits.QueryBy(filter)
                : repository.Commits.QueryBy(path, filter).Select(entry => entry.Commit);
            return commits.Take(take).Select(commit => Describe(repository, commit)).ToArray();
        }));

    public Task<ProjectVersion> RestoreAsync(ProjectBranch branch, string id, string? path = null) =>
        Task.FromResult(Guarded(branch, repository =>
        {
            var commit = Lookup(repository, id);
            var options = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };
            var source = Describe(repository, commit);
            string message;
            if (path is null)
            {
                SaveIfChanged(repository, null, BeforeRestoreMessage);
                repository.Checkout(commit.Tree, null, options);
                message = source.Label is { } label ? label.StartsWith("Restored ", StringComparison.Ordinal) ? label : $"Restored “{label}”"
                    : RestoredPrefix + commit.Committer.When.UtcDateTime.ToString("u");
            }
            else
            {
                if (commit[path]?.Target is not Blob) throw new WorkspaceException(WorkspaceError.NotFound, "That version does not contain this document.");
                SaveIfChanged(repository, null, BeforeRestoreMessage);
                repository.Checkout(commit.Tree, [path], options);
                message = $"Restored “{Path.GetFileName(path)}” from {commit.Committer.When.UtcDateTime:u}";
            }
            return Commit(repository, message, allowEmpty: true);
        }));

    public Task<string> ReadAsync(ProjectBranch branch, string id, string path) =>
        Task.FromResult(Guarded(branch, repository =>
        {
            var commit = Lookup(repository, id);
            if (commit[path]?.Target is not Blob blob) throw new WorkspaceException(WorkspaceError.NotFound, "That version does not contain this document.");
            using var stream = blob.GetContentStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return MarkdownDocumentCodec.Split(MarkdownDocumentCodec.Decode(memory.ToArray())).Body;
        }));

    public Task CreateBranchAsync(ProjectBranch branch, string name) => throw new NotSupportedException("Branches are not supported yet.");

    public Task MergeAsync(ProjectBranch from, ProjectBranch to) => throw new NotSupportedException("Branches are not supported yet.");

    public void Close(ProjectBranch branch)
    {
        lock (_repositories)
        {
            if (_repositories.Remove(branch.Project, out var repository)) repository.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_repositories)
        {
            foreach (var repository in _repositories.Values) repository.Dispose();
            _repositories.Clear();
        }
    }

    private Repository Open(ProjectBranch branch)
    {
        if (!branch.IsMain) throw new WorkspaceException(WorkspaceError.Invalid, "Only the main branch exists.");
        var name = ProjectNames.Validate(branch.Project);
        lock (_repositories)
        {
            if (_repositories.TryGetValue(name, out var open)) return open;
            var root = Path.Combine(_root, name);
            if (!Directory.Exists(root)) throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
            if (!Directory.Exists(Path.Combine(root, ".git"))) Repository.Init(root);
            var repository = new Repository(root);
            repository.Config.Set("core.autocrlf", "false");
            repository.Config.Set("core.safecrlf", "false");
            repository.Config.Set("core.filemode", "false");
            File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), string.Join('\n', Excluded) + '\n');
            return _repositories[name] = repository;
        }
    }

    private T Guarded<T>(ProjectBranch branch, Func<Repository, T> action)
    {
        try
        {
            var repository = Open(branch);
            lock (repository) return action(repository);
        }
        catch (LibGit2SharpException ex) { throw new WorkspaceException(WorkspaceError.Unavailable, "The project's versions could not be read or written: " + ex.Message); }
    }

    private static Commit Lookup(Repository repository, string id)
    {
        var commit = VersionId().IsMatch(id) ? repository.Lookup<Commit>(id) : null;
        return commit ?? throw new WorkspaceException(WorkspaceError.NotFound, "That version no longer exists.");
    }

    private static ProjectVersion? SaveIfChanged(Repository repository, string? label, string automaticMessage)
    {
        var changed = repository.RetrieveStatus(new StatusOptions()).IsDirty;
        if (!changed && label is null) return null;
        return Commit(repository, label ?? automaticMessage, allowEmpty: true);
    }

    private static ProjectVersion Commit(Repository repository, string message, bool allowEmpty)
    {
        Commands.Stage(repository, "*");
        var signature = new Signature("Odysseum", "odysseum@localhost", DateTimeOffset.Now);
        return Describe(repository, repository.Commit(message, signature, signature, new CommitOptions { AllowEmptyCommit = allowEmpty }));
    }

    private static ProjectVersion Describe(Repository repository, Commit commit)
    {
        var parent = commit.Parents.FirstOrDefault();
        var changes = repository.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree).Count;
        var automatic = commit.MessageShort is AutomaticMessage or BeforeRestoreMessage;
        return new(commit.Sha, commit.MessageShort == AutomaticMessage ? null : commit.MessageShort, automatic, commit.Committer.When, changes);
    }

    [GeneratedRegex("^[0-9a-f]{7,40}$")] private static partial Regex VersionId();
}
