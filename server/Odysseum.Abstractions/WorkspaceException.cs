namespace Odysseum.Abstractions;

/// <summary>Why a store refused an operation. The host maps these to whatever its transport needs.</summary>
public enum WorkspaceError
{
    /// <summary>The request itself is wrong: bad title, bad ID, unsupported name.</summary>
    Invalid,
    /// <summary>The document or folder is gone.</summary>
    NotFound,
    /// <summary>The revision is stale, or something else already has that place.</summary>
    Conflict,
    /// <summary>The content is over the store's size limit.</summary>
    TooLarge,
    /// <summary>The store's own records are damaged and need repair before it can proceed.</summary>
    Corrupt,
    /// <summary>The project is locked or otherwise not usable right now.</summary>
    Unavailable,
}

/// <summary>A storage failure with a writer-facing message.</summary>
public sealed class WorkspaceException(WorkspaceError error, string message) : Exception(message)
{
    public WorkspaceError Error { get; } = error;
}
