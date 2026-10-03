namespace Odysseum.Abstractions.Changes;

/// <summary>The changes of one write, or of one read of a project. All of them belong to the same project.</summary>
public sealed class ChangesEventArgs(IReadOnlyList<Change> changes) : EventArgs
{
    public IReadOnlyList<Change> Changes { get; } = changes;
}
