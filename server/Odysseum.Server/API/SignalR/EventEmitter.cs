using Microsoft.AspNetCore.SignalR;

namespace Odysseum.Server.API.SignalR;

/// <summary>The base of the emitters. An emitter listens to the events of one service and sends a message for each
/// event to the browsers.</summary>
public abstract class EventEmitter(IHubContext<ProjectHub> hub, ILogger logger)
{
    /// <summary>Sends the message to the browsers that have the project open.</summary>
    protected async void SendToProject(string projectId, string messageName, object message)
    {
        try { await hub.Clients.Group(ProjectHub.GroupName(projectId)).SendAsync(messageName, message); }
        catch (Exception ex) { logger.LogError(ex, "The {Message} message could not be sent.", messageName); }
    }

    /// <summary>Sends the message to all browsers.</summary>
    protected async void SendToAll(string messageName, object message)
    {
        try { await hub.Clients.All.SendAsync(messageName, message); }
        catch (Exception ex) { logger.LogError(ex, "The {Message} message could not be sent.", messageName); }
    }
}
