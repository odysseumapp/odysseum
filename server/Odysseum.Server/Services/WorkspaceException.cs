namespace Odysseum.Server.Services;

public sealed class WorkspaceException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
