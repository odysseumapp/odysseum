using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Models.Editing;

/// <summary>Edits the documents of a copy of a project and marks what changed. Link edits are mirrored on the other
/// documents, which are marked too. <see cref="Result"/> is the edited project; <see cref="Changes"/> is what storage
/// must save.</summary>
public sealed class DocumentEditor
{
    private readonly ProjectDraft _draft;

    public DocumentEditor(Project project) : this(new ProjectDraft(project)) { }

    internal DocumentEditor(ProjectDraft draft)
    {
        _draft = draft;
    }

    /// <summary>A folder editor on the same working copy.</summary>
    public FolderEditor Folders => new(_draft);

    public Document Create(string folderId, string title, string? body = null, string? fileName = null)
    {
        title = ValidateTitle(title);
        _draft.Folder(folderId);
        var name = fileName ?? FileName(title) + ".md";
        if (!IsDocument(name) || name.StartsWith('.')) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
        var path = Item.Join(_draft.PathOf(folderId), name);
        var text = string.IsNullOrEmpty(body) ? StarterContent(path) : body;
        var document = new Document(Guid.NewGuid().ToString(), name, title,
            wordGoal: IsFolderDocument(path) ? 0 : _draft.Project.DefaultSceneWordGoal,
            modified: DateTimeOffset.UtcNow, wordCount: CountWords(text), body: text);
        _draft.Put(document);
        _draft.Attach(document.Id, folderId, int.MaxValue);
        return document;
    }

    public void SetBody(string id, string body)
    {
        if (body is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document content is required.");
        _draft.Put(_draft.Document(id).WithBody(body));
    }

    /// <summary>Changes the details. Null fields keep their value. <c>Links</c> is the complete set; the other
    /// documents are updated so every link is present on both sides. <c>LinkNotes</c> is the complete set of notes;
    /// a note for a document that is not linked is dropped.</summary>
    public void SetDetails(string id, DocumentDetails details)
    {
        var current = _draft.Document(id);
        details = Validate(current, details);
        var document = current.WithDetails(details);
        var before = current.Links;
        var links = details.Links ?? before;
        var notes = new Dictionary<string, string>(current.LinkNotes, StringComparer.Ordinal);
        foreach (var removed in before.Except(links, StringComparer.Ordinal))
        {
            var other = _draft.Document(removed);
            _draft.Put(other.WithLinks(Without(other.Links, id), Without(other.LinkNotes, id)));
            notes.Remove(removed);
        }
        foreach (var added in links.Except(before, StringComparer.Ordinal))
        {
            var other = _draft.Document(added);
            if (!other.Links.Contains(id, StringComparer.Ordinal)) _draft.Put(other.WithLinks([.. other.Links, id], other.LinkNotes));
        }
        if (details.LinkNotes is not null)
        {
            foreach (var linked in links)
            {
                var note = details.LinkNotes.GetValueOrDefault(linked)?.Trim() ?? "";
                var other = _draft.Document(linked);
                if (note.Length == 0)
                {
                    notes.Remove(linked);
                    if (other.LinkNotes.ContainsKey(id)) _draft.Put(other.WithLinks(other.Links, Without(other.LinkNotes, id)));
                }
                else
                {
                    notes[linked] = note;
                    if (other.LinkNotes.GetValueOrDefault(id) != note)
                        _draft.Put(other.WithLinks(other.Links, new Dictionary<string, string>(other.LinkNotes, StringComparer.Ordinal) { [id] = note }));
                }
            }
        }
        _draft.Put(document.WithLinks([.. links], notes));
    }

    /// <summary>Moves the document to another folder, renames it, or both. A null file name keeps the current one.</summary>
    public void Move(string id, string targetFolderId, string? fileName)
    {
        var document = _draft.Document(id);
        if (_draft.IsOwnDocument(id)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder.");
        _draft.Folder(targetFolderId);
        var name = fileName ?? document.Name;
        if (!IsDocument(name) || name.StartsWith('.') || name.Contains('/')) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
        if (_draft.ParentOf(id) != targetFolderId)
        {
            _draft.Detach(id);
            _draft.Attach(id, targetFolderId, int.MaxValue);
        }
        if (name != document.Name) _draft.Put(document.WithName(name));
        else _draft.Touch(id);
    }

    public Project Result() => _draft.Build();

    public Changes Changes() => _draft.Changes();

    private DocumentDetails Validate(Document current, DocumentDetails details)
    {
        var title = details.Title is null ? null : ValidateTitle(details.Title);
        if (details.Status is { } status && !Enum.IsDefined(status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        if (details.WordGoal is < 0 or > 10000000) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid word goal.");
        if (details.Synopsis?.Length > 20000 || details.Notes?.Length > 100000) throw new WorkspaceException(WorkspaceError.Invalid, "Notes are too long.");
        IReadOnlyList<string>? links = null;
        if (details.Links is not null)
        {
            if (details.Links.Count > 200) throw new WorkspaceException(WorkspaceError.Invalid, "Too many links attached.");
            links = details.Links.Distinct(StringComparer.Ordinal).ToArray();
            if (links.Any(id => id is null || id == current.Id || !_draft.HasDocument(id)))
                throw new WorkspaceException(WorkspaceError.Invalid, "One of the linked documents no longer exists.");
        }
        if (details.LinkNotes is not null && details.LinkNotes.Values.Any(note => note is null || note.Length > 2000))
            throw new WorkspaceException(WorkspaceError.Invalid, "A link note is too long.");
        return details with { Title = title, Links = links };
    }

    private static IReadOnlyList<string> Without(IReadOnlyList<string> ids, string id) => ids.Where(x => x != id).ToArray();

    private static IReadOnlyDictionary<string, string> Without(IReadOnlyDictionary<string, string> notes, string id)
    {
        var copy = new Dictionary<string, string>(notes, StringComparer.Ordinal);
        copy.Remove(id);
        return copy;
    }
}
