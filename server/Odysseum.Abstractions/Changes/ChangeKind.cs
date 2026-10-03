namespace Odysseum.Abstractions.Changes;

/// <summary>What happened to an item. <see cref="Moved"/> is only for an item that a move put in another folder; a
/// rename, and the items inside a moved folder, are <see cref="Updated"/>.</summary>
public enum ChangeKind
{
    Added,
    Updated,
    Moved,
    Removed,
}
