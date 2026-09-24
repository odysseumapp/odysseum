using Odysseum.Server.API.Models;

namespace Odysseum.Server.Repositories;

public interface IProjectVersionRepository : IDisposable
{
    VersionInfo? Save(string? name);
    IReadOnlyList<VersionInfo> List(int take = 100);
    VersionInfo Restore(string id);
}
