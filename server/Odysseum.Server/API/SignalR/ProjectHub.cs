using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.API.SignalR;

/// <summary>The live connection to the browsers, at <c>/api/hub</c>. A browser calls <see cref="OpenProject"/> for the
/// project it shows, and then gets <c>changed</c> messages for the changes to that project's folders, documents and
/// links, also changes from other browsers and other programs. Every browser gets the changes to the projects themselves.</summary>
public sealed class ProjectHub(IProjectService projects) : Hub
{
    public async Task OpenProject(string projectId)
    {
        await projects.GetAsync<IProject>(projectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(projectId));
    }

    public Task CloseProject(string projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(projectId));

    public static string GroupName(string projectId) => "project:" + projectId;
}
