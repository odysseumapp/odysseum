using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.API.SignalR;

/// <summary>The live connection to the browsers, at <c>/api/hub</c>. A browser calls <see cref="OpenProject"/> for the
/// project it shows, and then gets a message for each change to that project, also changes from other browsers and
/// other programs. Every browser gets the messages about created and removed projects.</summary>
public sealed class ProjectHub(IProjectService projects) : Hub
{
    public async Task OpenProject(string projectId)
    {
        await projects.GetProjectByIdAsync(projectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(projectId));
    }

    public Task CloseProject(string projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(projectId));

    public static string GroupName(string projectId) => "project:" + projectId;
}
