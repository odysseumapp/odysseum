namespace Odysseum.Server.API.SignalR.Models;

public record ChangedMessage(string ProjectId, IReadOnlyList<ChangeMessage> Changes);
