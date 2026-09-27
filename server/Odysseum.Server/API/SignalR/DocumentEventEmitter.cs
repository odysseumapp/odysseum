using Microsoft.AspNetCore.SignalR;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Documents.Events;
using Odysseum.Server.API.SignalR.Models;

namespace Odysseum.Server.API.SignalR;

/// <summary>Sends <c>documentCreated</c>, <c>documentUpdated</c>, <c>documentMoved</c> and <c>documentRemoved</c> to the
/// browsers that have the document's project open.</summary>
public sealed class DocumentEventEmitter : EventEmitter, IDisposable
{
    private readonly IDocumentService _documents;

    public DocumentEventEmitter(IHubContext<ProjectHub> hub, IDocumentService documents, ILogger<DocumentEventEmitter> logger) : base(hub, logger)
    {
        _documents = documents;
        documents.DocumentCreated += OnDocumentCreated;
        documents.DocumentUpdated += OnDocumentUpdated;
        documents.DocumentMoved += OnDocumentMoved;
        documents.DocumentRemoved += OnDocumentRemoved;
    }

    public void Dispose()
    {
        _documents.DocumentCreated -= OnDocumentCreated;
        _documents.DocumentUpdated -= OnDocumentUpdated;
        _documents.DocumentMoved -= OnDocumentMoved;
        _documents.DocumentRemoved -= OnDocumentRemoved;
    }

    private void OnDocumentCreated(object? sender, DocumentEventArgs e) => Send("documentCreated", e);
    private void OnDocumentUpdated(object? sender, DocumentEventArgs e) => Send("documentUpdated", e);
    private void OnDocumentMoved(object? sender, DocumentEventArgs e) => Send("documentMoved", e);
    private void OnDocumentRemoved(object? sender, DocumentEventArgs e) => Send("documentRemoved", e);

    private void Send(string messageName, DocumentEventArgs e) =>
        SendToProject(e.Document.ProjectId, messageName, new DocumentChangedMessage(e.Document.ProjectId, e.Document.Id, e.Document.ETag));
}
