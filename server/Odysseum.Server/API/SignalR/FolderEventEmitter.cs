using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Folders.Events;
using Odysseum.Server.API.SignalR.Models;

namespace Odysseum.Server.API.SignalR;

/// <summary>Sends <c>folderCreated</c>, <c>folderUpdated</c>, <c>folderMoved</c> and <c>folderRemoved</c> to the browsers
/// that have the folder's project open.</summary>
public sealed class FolderEventEmitter : EventEmitter, IDisposable
{
    private readonly IFolderService _folders;

    public FolderEventEmitter(IHubContext<ProjectHub> hub, IFolderService folders, ILogger<FolderEventEmitter> logger) : base(hub, logger)
    {
        _folders = folders;
        folders.FolderCreated += OnFolderCreated;
        folders.FolderUpdated += OnFolderUpdated;
        folders.FolderMoved += OnFolderMoved;
        folders.FolderRemoved += OnFolderRemoved;
    }

    public void Dispose()
    {
        _folders.FolderCreated -= OnFolderCreated;
        _folders.FolderUpdated -= OnFolderUpdated;
        _folders.FolderMoved -= OnFolderMoved;
        _folders.FolderRemoved -= OnFolderRemoved;
    }

    private void OnFolderCreated(object? sender, FolderEventArgs e) => Send("folderCreated", e);
    private void OnFolderUpdated(object? sender, FolderEventArgs e) => Send("folderUpdated", e);
    private void OnFolderMoved(object? sender, FolderEventArgs e) => Send("folderMoved", e);
    private void OnFolderRemoved(object? sender, FolderEventArgs e) => Send("folderRemoved", e);

    private void Send(string messageName, FolderEventArgs e) =>
        SendToProject(e.Folder.ProjectId, messageName, new FolderChangedMessage(e.Folder.ProjectId, e.Folder.Id, e.Folder.ETag));
}
