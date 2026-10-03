namespace Odysseum.Server.Plugins;

public sealed class PluginRepository(IReadOnlyList<InstalledPlugin> plugins) : IPluginRepository
{
    public IReadOnlyList<InstalledPlugin> GetAll() => plugins;
}
