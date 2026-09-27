namespace Odysseum.Abstractions.Links.Events;

public sealed class LinkEventArgs(ILink link) : EventArgs
{
    public ILink Link { get; } = link;
}
