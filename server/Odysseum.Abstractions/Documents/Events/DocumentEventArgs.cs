namespace Odysseum.Abstractions.Documents.Events;

public sealed class DocumentEventArgs(IDocument document) : EventArgs
{
    public IDocument Document { get; } = document;
}
