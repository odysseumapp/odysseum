using Odysseum.Server.Repositories.Manifests;

namespace Odysseum.Server.Repositories;

public interface IProjectManifestRepository
{
    Task<(ProjectManifest? Manifest, string Revision)> ReadAsync();
    Task<string> WriteAsync(ProjectManifest candidate, string expectedRevision);
}
