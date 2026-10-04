namespace Odysseum.Abstractions.Plugins;

/// <summary>What a plugin can set up in <see cref="IPlugin.Register"/>. The server implements it; plugins only call it,
/// so it can get new members without a change to existing plugins. It has no members yet: views need no setup, because
/// the server finds each <see cref="Views.IViewDefinition"/> class by itself.</summary>
public interface IPluginRegistry;
