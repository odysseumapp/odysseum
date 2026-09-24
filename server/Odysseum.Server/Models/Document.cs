using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services.Documents;

namespace Odysseum.Server.Models;

public sealed class Document : IDocument
{
    private IReadOnlyList<Document>? _links;

    internal Document(Project project, Folder folder, DiskDocument disk, DocumentMetadata metadata)
    {
        Owner = project;
        Location = folder;
        Disk = disk;
        Metadata = metadata;
        Title = metadata.Title;
        Synopsis = metadata.Synopsis;
        Notes = metadata.Notes;
        Status = metadata.Status;
        WordGoal = metadata.WordGoal;
        Body = disk.Body;
        LinkIds = metadata.Links;
        LinkNotes = metadata.LinkNotes;
    }

    private Document(Document source, DocumentDetails? details, string? body)
    {
        Owner = source.Owner;
        Location = source.Location;
        Disk = source.Disk;
        Metadata = source.Metadata;
        Title = details?.Title ?? source.Title;
        Synopsis = details?.Synopsis ?? source.Synopsis;
        Notes = details?.Notes ?? source.Notes;
        Status = details?.Status ?? source.Status;
        WordGoal = details?.WordGoal ?? source.WordGoal;
        LinkIds = details?.Links ?? source.LinkIds;
        LinkNotes = details?.LinkNotes ?? source.LinkNotes;
        Body = body ?? source.Body;
    }

    internal Project Owner { get; }
    internal Folder Location { get; }
    internal DiskDocument Disk { get; }
    internal DocumentMetadata Metadata { get; }
    public string Id => Disk.Id;
    public IProject Project => Owner;
    public IFolder Folder => Location;
    public string Path => Disk.Path;
    public string FileName => System.IO.Path.GetFileName(Disk.Path);
    public bool IsFolderDocument => DocumentRules.IsFolderDocument(Disk.Path);
    public DocumentKind Kind => DocumentRules.KindOf(Disk.Path);
    public string Title { get; }
    public string Synopsis { get; }
    public string Notes { get; }
    public DocumentStatus Status { get; }
    public int WordGoal { get; }
    public IReadOnlyList<string> LinkIds { get; private set; }
    public IReadOnlyDictionary<string, string> LinkNotes { get; private set; }
    public IReadOnlyList<IDocument> Links => _links ??= LinkIds.Select(Owner.Document).OfType<Document>().ToArray();
    public string Body { get; }
    public string Revision => Disk.Revision;
    public DateTimeOffset Modified => new(DateTime.SpecifyKind(Disk.Modified, DateTimeKind.Utc));

    public string? LinkNote(IDocument other) => LinkNotes.GetValueOrDefault(other.Id);

    internal void Resolve(IReadOnlyList<string> reverse)
    {
        LinkIds = Metadata.Links.Where(id => Owner.Document(id) is not null).Concat(reverse).Distinct().ToArray();
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var other in LinkIds)
        {
            if ((Metadata.LinkNotes.TryGetValue(other, out var note)
                || Owner.Manifest.Documents[other].LinkNotes.TryGetValue(Id, out note)) && note.Length > 0) notes[other] = note;
        }
        LinkNotes = notes;
    }

    internal Document With(DocumentDetails details) => new(this, details, null);
    internal Document WithBody(string body) => new(this, null, body);
}
