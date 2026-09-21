using System.Text.RegularExpressions;
using LibGit2Sharp;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.Services.Versioning;

/// <summary>
/// Whole-project versions, kept as a git repository in the project folder. Every version is a commit on one
/// branch; a restore writes the old files back and records that as a new version, so nothing is ever lost.
/// Called under the project's lock: the repository handle is not thread safe.
/// </summary>
internal sealed partial class ProjectVersionStore : IDisposable
{
    private const string AutomaticMessage = "Automatic version";
    private const string RestoredPrefix = "Restored the version from ";
    // Recovery snapshots, the process lock, and transaction scratch are not project content.
    private static readonly string[] Excluded =
    [
        "/.odysseum/history/", "/.odysseum/instance.lock", "/.odysseum/pending-manifests.json",
        "/.odysseum/manifest-transaction/", "/.odysseum/removed-folders/", ".odysseum-*.tmp",
    ];
    private readonly Repository _repository;

    static ProjectVersionStore()
    {
        // In a container the project volume is rarely owned by the server's user, which libgit2 otherwise refuses.
        GlobalSettings.SetOwnerValidation(false);
    }

    public ProjectVersionStore(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".git"))) Repository.Init(root);
        _repository = new Repository(root);
        // Files must round-trip byte for byte whatever the user's global git settings say.
        _repository.Config.Set("core.autocrlf", "false");
        _repository.Config.Set("core.safecrlf", "false");
        _repository.Config.Set("core.filemode", "false");
        File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), string.Join('\n', Excluded) + '\n');
    }

    /// <summary>Records the current files as a version. Without a name, nothing is recorded unless something changed.</summary>
    public VersionInfo? Save(string? name) => Guarded(() =>
    {
        var changed = _repository.RetrieveStatus(new StatusOptions()).IsDirty;
        if (!changed && name is null) return null;
        return Commit(name ?? AutomaticMessage, allowEmpty: true);
    });

    public IReadOnlyList<VersionInfo> List(int take = 100) => Guarded<IReadOnlyList<VersionInfo>>(() =>
    {
        if (_repository.Head.Tip is null) return [];
        // Time order ties within a second; topological order follows the parent chain regardless of clock.
        var newestFirst = _repository.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Topological });
        return newestFirst.Take(take).Select(Describe).ToArray();
    });

    /// <summary>Writes a version's files over the working folder and records the result as a new version.</summary>
    public VersionInfo Restore(string id) => Guarded(() =>
    {
        var commit = VersionId().IsMatch(id) ? _repository.Lookup<Commit>(id) : null;
        if (commit is null) throw new WorkspaceException(404, "That version no longer exists.");
        Save(null); // The state being replaced stays recoverable.
        _repository.Checkout(commit.Tree, null, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        var source = Describe(commit);
        return Commit(source.Name is { } name ? $"Restored “{name}”" : RestoredPrefix + commit.Committer.When.UtcDateTime.ToString("u"), allowEmpty: true);
    });

    /// <summary>libgit2 reports a read-only or damaged repository through its own exception type; the API knows the workspace one.</summary>
    private static T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (LibGit2SharpException ex) { throw new WorkspaceException(503, "The project's versions could not be read or written: " + ex.Message); }
    }

    private VersionInfo Commit(string message, bool allowEmpty)
    {
        Commands.Stage(_repository, "*");
        var signature = new Signature("Odysseum", "odysseum@localhost", DateTimeOffset.Now);
        return Describe(_repository.Commit(message, signature, signature, new CommitOptions { AllowEmptyCommit = allowEmpty }));
    }

    private VersionInfo Describe(Commit commit)
    {
        var parent = commit.Parents.FirstOrDefault();
        var changes = _repository.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree).Count;
        var automatic = commit.MessageShort == AutomaticMessage;
        return new(commit.Sha, automatic ? null : commit.MessageShort, automatic, commit.Committer.When.UtcDateTime, changes);
    }

    public void Dispose() => _repository.Dispose();

    [GeneratedRegex("^[0-9a-f]{7,40}$")] private static partial Regex VersionId();
}
