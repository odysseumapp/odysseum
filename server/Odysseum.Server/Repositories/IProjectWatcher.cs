namespace Odysseum.Server.Repositories;

/// <summary>Reports changes that other programs make to a watched project folder. Changes this process wrote are not
/// reported. A project is named by its folder name in the workspace.</summary>
public interface IProjectWatcher : IAsyncDisposable
{
    event Action<string>? Changed;
    void Watch(string projectName);
    void Unwatch(string projectName);
}
