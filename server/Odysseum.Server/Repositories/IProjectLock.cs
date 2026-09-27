namespace Odysseum.Server.Repositories;

/// <summary>Runs work while no other work changes the same project. A service uses it when one task makes several
/// changes that must not mix with other changes. The lock can be taken again inside the work.</summary>
public interface IProjectLock
{
    Task<T> RunLockedAsync<T>(string projectId, Func<Task<T>> work);
    Task RunLockedAsync(string projectId, Func<Task> work);
}
