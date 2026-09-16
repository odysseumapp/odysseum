using System.Collections.Concurrent;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Odysseum.Server.Services.WebUi;

/// <summary>Resolves the active immutable release on each request, allowing updates without restarting the API.</summary>
public sealed class WebUiFileProvider(WebUiInstallation installation) : IFileProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, PhysicalFileProvider> _providers = new();
    private PhysicalFileProvider? Provider(string? directory) => directory is null ? null
        : _providers.GetOrAdd(directory, path => new PhysicalFileProvider(path));

    public IFileInfo GetFileInfo(string subpath)
    {
        var current = Provider(installation.CurrentDirectory)?.GetFileInfo(subpath);
        if (current?.Exists == true) return current;
        // Existing tabs may still request lazy chunks from the preceding release.
        if (subpath.StartsWith("/_nuxt/", StringComparison.Ordinal))
        {
            var previous = Provider(installation.PreviousDirectory)?.GetFileInfo(subpath);
            if (previous?.Exists == true) return previous;
        }
        return new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) =>
        Provider(installation.CurrentDirectory)?.GetDirectoryContents(subpath) ?? NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    public void Dispose() { foreach (var provider in _providers.Values) provider.Dispose(); }
}
