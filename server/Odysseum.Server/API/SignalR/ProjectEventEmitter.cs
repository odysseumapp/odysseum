using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Projects;
using Odysseum.Abstractions.Projects.Events;
using Odysseum.Server.API.SignalR.Models;

namespace Odysseum.Server.API.SignalR;

/// <summary>Sends <c>projectCreated</c> and <c>projectRemoved</c> to all browsers, and <c>projectUpdated</c> to the
/// browsers that have the project open.</summary>
public sealed class ProjectEventEmitter : EventEmitter, IDisposable
{
    private readonly IProjectService _projects;

    public ProjectEventEmitter(IHubContext<ProjectHub> hub, IProjectService projects, ILogger<ProjectEventEmitter> logger) : base(hub, logger)
    {
        _projects = projects;
        projects.ProjectCreated += OnProjectCreated;
        projects.ProjectUpdated += OnProjectUpdated;
        projects.ProjectRemoved += OnProjectRemoved;
    }

    public void Dispose()
    {
        _projects.ProjectCreated -= OnProjectCreated;
        _projects.ProjectUpdated -= OnProjectUpdated;
        _projects.ProjectRemoved -= OnProjectRemoved;
    }

    private void OnProjectCreated(object? sender, ProjectEventArgs e) => SendToAll("projectCreated", Message(e));
    private void OnProjectUpdated(object? sender, ProjectEventArgs e) => SendToProject(e.Project.Id, "projectUpdated", Message(e));
    private void OnProjectRemoved(object? sender, ProjectEventArgs e) => SendToAll("projectRemoved", Message(e));

    private static ProjectChangedMessage Message(ProjectEventArgs e) => new(e.Project.Id, e.Project.ETag);
}
