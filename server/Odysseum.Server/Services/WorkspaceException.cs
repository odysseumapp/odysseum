namespace Odysseum.Server.Services;

/// <summary>A storage failure with a writer-facing message; the API middleware turns it into an error response.</summary>
public sealed class WorkspaceException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
