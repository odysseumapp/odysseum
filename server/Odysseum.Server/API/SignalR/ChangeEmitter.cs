using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.SignalR.Models;

namespace Odysseum.Server.API.SignalR;

/// <summary>Sends <c>changed</c> messages for each batch of changes. The changes to the project itself go to all
/// browsers, so that a project list sees projects that are added, updated or removed. The other changes go to the
/// browsers that have the project open. A message only names the changed items; the browser reads them again.</summary>
public sealed class ChangeEmitter : IDisposable
{
    private readonly IHubContext<ProjectHub> _hub;
    private readonly IProjectService _projects;
    private readonly ILogger<ChangeEmitter> _logger;

    public ChangeEmitter(IHubContext<ProjectHub> hub, IProjectService projects, ILogger<ChangeEmitter> logger)
    {
        _hub = hub;
        _projects = projects;
        _logger = logger;
        projects.Changed += OnChanged;
    }

    public void Dispose() => _projects.Changed -= OnChanged;

    private async void OnChanged(object? sender, ChangesEventArgs e)
    {
        var projectId = e.Changes[0].ProjectId;
        var projectChanges = e.Changes.Where(change => change.Type == ItemType.Project).ToArray();
        var itemChanges = e.Changes.Where(change => change.Type != ItemType.Project).ToArray();
        try
        {
            if (projectChanges.Length > 0) await _hub.Clients.All.SendAsync("changed", Message(projectId, projectChanges));
            if (itemChanges.Length > 0) await _hub.Clients.Group(ProjectHub.GroupName(projectId)).SendAsync("changed", Message(projectId, itemChanges));
        }
        catch (Exception ex) { _logger.LogError(ex, "The changes of project {Project} could not be sent.", projectId); }
    }

    private static ChangedMessage Message(string projectId, IEnumerable<Change> changes) =>
        new(projectId, [.. changes.Select(change => new ChangeMessage(change.Type, change.Kind, change.Id, change.ETag))]);
}
