using System.Text.RegularExpressions;
using LibGit2Sharp;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.Services.Versioning;

internal sealed partial class ProjectVersionStore : IDisposable
{
    private const string AutomaticMessage = "Automatic version";
    private const string BeforeRestoreMessage = "Before restoring";
    private const string RestoredPrefix = "Restored the version from ";
    private static readonly string[] Excluded =
    [
        "/.odysseum/history/", "/.odysseum/instance.lock", "/.odysseum/pending-manifests.json",
        "/.odysseum/manifest-transaction/", "/.odysseum/removed-folders/", ".odysseum-*.tmp",
    ];
    private readonly Repository _repository;

    static ProjectVersionStore()
    {
        GlobalSettings.SetOwnerValidation(false);
    }

    public ProjectVersionStore(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".git"))) Repository.Init(root);
        _repository = new Repository(root);
        _repository.Config.Set("core.autocrlf", "false");
        _repository.Config.Set("core.safecrlf", "false");
        _repository.Config.Set("core.filemode", "false");
        File.WriteAllText(Path.Combine(root, ".git", "info", "exclude"), string.Join('\n', Excluded) + '\n');
    }

    public VersionInfo? Save(string? name) => Guarded(() => SaveIfChanged(name, AutomaticMessage));

    private VersionInfo? SaveIfChanged(string? name, string automaticMessage)
    {
        var changed = _repository.RetrieveStatus(new StatusOptions()).IsDirty;
        if (!changed && name is null) return null;
        return Commit(name ?? automaticMessage, allowEmpty: true);
    }

    public IReadOnlyList<VersionInfo> List(int take = 100) => Guarded<IReadOnlyList<VersionInfo>>(() =>
    {
        if (_repository.Head.Tip is null) return [];
        var newestFirst = _repository.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Topological });
        return newestFirst.Take(take).Select(Describe).ToArray();
    });

    public VersionInfo Restore(string id) => Guarded(() =>
    {
        var commit = VersionId().IsMatch(id) ? _repository.Lookup<Commit>(id) : null;
        if (commit is null) throw new WorkspaceException(404, "That version no longer exists.");
        SaveIfChanged(null, BeforeRestoreMessage);
        _repository.Checkout(commit.Tree, null, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        var source = Describe(commit);
        var message = source.Name is { } name ? name.StartsWith("Restored ", StringComparison.Ordinal) ? name : $"Restored “{name}”"
            : RestoredPrefix + commit.Committer.When.UtcDateTime.ToString("u");
        return Commit(message, allowEmpty: true);
    });

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
        var automatic = commit.MessageShort is AutomaticMessage or BeforeRestoreMessage;
        return new(commit.Sha, commit.MessageShort == AutomaticMessage ? null : commit.MessageShort, automatic, commit.Committer.When.UtcDateTime, changes);
    }

    public void Dispose() => _repository.Dispose();

    [GeneratedRegex("^[0-9a-f]{7,40}$")] private static partial Regex VersionId();
}
