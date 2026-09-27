namespace Odysseum.Abstractions.Exceptions;

public enum WorkspaceError
{
    Invalid,
    NotFound,
    Conflict,
    TooLarge,
    Corrupt,
    Unavailable,
    Forbidden,
    /// <summary>The item changed after the caller read it: the ETag the caller gave is not the current ETag.</summary>
    ETagMismatch,
}
