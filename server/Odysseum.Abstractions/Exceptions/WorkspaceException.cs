namespace Odysseum.Abstractions.Exceptions;

public sealed class WorkspaceException(WorkspaceError error, string message) : Exception(message)
{
    public WorkspaceError Error { get; } = error;
}
