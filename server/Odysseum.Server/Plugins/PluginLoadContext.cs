using System.Reflection;
using System.Runtime.Loader;

namespace Odysseum.Server.Plugins;

/// <summary>The load context of one plugin. It loads the plugin's own dependencies from its folder, as its
/// <c>.deps.json</c> lists them. An assembly that the plugin does not ship, such as <c>Odysseum.Abstractions</c>, comes
/// from the server, so the plugin and the server share its types.</summary>
internal sealed class PluginLoadContext(string assemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(assemblyPath))
{
    private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName) =>
        _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
}
