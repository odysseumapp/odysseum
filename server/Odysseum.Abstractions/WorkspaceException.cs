namespace Odysseum.Abstractions;

public enum WorkspaceError
{
    Invalid,
    NotFound,
    Conflict,
    TooLarge,
    Corrupt,
    Unavailable,
}

public sealed class WorkspaceException(WorkspaceError error, string message) : Exception(message)
{
    public WorkspaceError Error { get; } = error;
}
