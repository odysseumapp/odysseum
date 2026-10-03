using Odysseum.Abstractions.Plugins;
using Odysseum.Abstractions.Views;

namespace Odysseum.Server.Plugins;

/// <summary>Collects what one plugin adds while its <see cref="IPlugin.Register"/> runs.</summary>
internal sealed class PluginRegistry : IPluginRegistry
{
    public List<IViewDefinition> Views { get; } = [];

    public void AddView(IViewDefinition view) => Views.Add(view ?? throw new ArgumentNullException(nameof(view)));
}
