using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Links;
using Odysseum.Abstractions.Links.Events;
using Odysseum.Server.API.SignalR.Models;

namespace Odysseum.Server.API.SignalR;

/// <summary>Sends <c>linkCreated</c>, <c>linkUpdated</c> and <c>linkRemoved</c> to the browsers that have the link's
/// project open.</summary>
public sealed class LinkEventEmitter : EventEmitter, IDisposable
{
    private readonly ILinkService _links;

    public LinkEventEmitter(IHubContext<ProjectHub> hub, ILinkService links, ILogger<LinkEventEmitter> logger) : base(hub, logger)
    {
        _links = links;
        links.LinkCreated += OnLinkCreated;
        links.LinkUpdated += OnLinkUpdated;
        links.LinkRemoved += OnLinkRemoved;
    }

    public void Dispose()
    {
        _links.LinkCreated -= OnLinkCreated;
        _links.LinkUpdated -= OnLinkUpdated;
        _links.LinkRemoved -= OnLinkRemoved;
    }

    private void OnLinkCreated(object? sender, LinkEventArgs e) => Send("linkCreated", e);
    private void OnLinkUpdated(object? sender, LinkEventArgs e) => Send("linkUpdated", e);
    private void OnLinkRemoved(object? sender, LinkEventArgs e) => Send("linkRemoved", e);

    private void Send(string messageName, LinkEventArgs e) =>
        SendToProject(e.Link.ProjectId, messageName, new LinkChangedMessage(e.Link.ProjectId, e.Link.Id, e.Link.ETag));
}
