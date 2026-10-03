using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Links;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Plugins;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Disk;
using Odysseum.Server.Repositories.Git;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Settings;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<Check, Task> Run)>
{
    ("A new project gets the Default template's folders, each with its own hidden document", async c =>
    {
        var root = await c.Projects.GetAsync<IFolder>(c.Project.RootFolderId);
        var names = new List<string>();
        foreach (var id in root.ChildIds) names.Add((await c.Projects.GetAsync<IFolder>(id)).Name);
        Require(names.SequenceEqual(["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"]), "The top folders are not the default folders in order.");
        foreach (var folder in await c.Projects.GetAllAsync<IFolder>(c.Project.Id))
        {
            if (folder.ParentFolderId is null) continue;
            Require(folder.OwnDocumentId is not null, $"The folder {folder.Name} has no own document.");
            Require(File.Exists(Path.Combine(c.Root, ((Folder)folder).Path, $".{folder.Name}.md")), $"The own document of {folder.Name} is not on the disk.");
        }
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        Require(scene.Kind == DocumentKind.Scene && scene.WordGoal == 1000, "The template's scene is missing or has the wrong kind or word goal.");
        Require((await c.DocumentAt("Styles/Default.md")).Kind == DocumentKind.Style, "The template's style sheet is missing.");
    }),
    ("The paths of documents and folders are saved in documents.json and folders.json, and a move changes them", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var documents = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "documents.json")))!["paths"]!;
        var folders = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "folders.json")))!["paths"]!;
        Require(documents[scene.Id]!.GetValue<string>() == "Manuscript/Chapter 01/Scene 01.md", "The document's path is not in documents.json.");
        Require(folders[chapter.Id]!.GetValue<string>() == "Manuscript/Chapter 01", "The folder's path is not in folders.json.");
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["documents"]![scene.Id]!;
        Require(entry["title"] is not null && entry["fileName"] is null && entry["path"] is null, "folder.json still has a file name, or has no details.");
        var notes = await c.FolderAt("Notes");
        var moved = await c.Repository.MoveDocumentAsync(scene.Id, notes.Id, 0, scene.ETag);
        documents = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "documents.json")))!["paths"]!;
        Require(File.Exists(Path.Combine(c.Root, "Notes", "Scene 01.md")) && moved.Path == "Notes/Scene 01.md"
            && documents[scene.Id]!.GetValue<string>() == "Notes/Scene 01.md", "The move did not move the file and change its path.");
        Require(moved.FolderId == notes.Id && moved.Kind == DocumentKind.Note && moved.Title == scene.Title, "The move did not keep the document's details.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Repository.MoveDocumentAsync(scene.Id, chapter.Id, 0, scene.ETag));
    }),
    ("A move to an index in another folder is one write that saves the target's order", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var a = await c.Projects.CreateDocumentAsync(notes.Id, "A");
        var b = await c.Projects.CreateDocumentAsync(notes.Id, "B");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var batches = 0;
        c.Repository.Changed += (_, _) => batches++;
        var moved = await c.Repository.MoveDocumentAsync(scene.Id, notes.Id, 1, scene.ETag);
        var order = (await c.Repository.GetAsync<Folder>(notes.Id))!.ChildIds;
        Require(batches == 1 && moved.FolderId == notes.Id && order.SequenceEqual([a.Id, scene.Id, b.Id]), "The document is not at the index in one write.");
        Require(SavedOrder(c.Root, "Notes").SequenceEqual(order), "The new order of the target folder was not saved.");
        Require(!(await c.Repository.GetAsync<Folder>(chapter.Id))!.ChildIds.Contains(scene.Id), "The document is still a child of its old folder.");

        var inner = await c.Projects.CreateFolderAsync(chapter.Id, "Inner");
        batches = 0;
        var movedFolder = await c.Repository.MoveFolderAsync(inner.Id, notes.Id, 0, inner.ETag);
        order = (await c.Repository.GetAsync<Folder>(notes.Id))!.ChildIds;
        Require(batches == 1 && movedFolder.ParentFolderId == notes.Id && movedFolder.Path == "Notes/Inner" && order[0] == inner.Id,
            "The folder is not at the index in one write.");
        Require(SavedOrder(c.Root, "Notes").SequenceEqual(order), "The new order of the target folder was not saved after a folder move.");
    }),
    ("A move inside the same folder only changes the order", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var a = await c.Projects.CreateDocumentAsync(notes.Id, "A");
        var b = await c.Projects.CreateDocumentAsync(notes.Id, "B");
        var inner = await c.Projects.CreateFolderAsync(notes.Id, "Inner");
        var batches = new List<IReadOnlyList<Change>>();
        c.Repository.Changed += (_, e) => batches.Add(e.Changes);
        var moved = await c.Repository.MoveDocumentAsync(b.Id, notes.Id, 0, b.ETag);
        Require(moved.ETag == b.ETag && moved.Path == "Notes/B.md", "A move inside the folder changed the document.");
        Require((await c.Repository.GetAsync<Folder>(notes.Id))!.ChildIds.SequenceEqual([b.Id, a.Id, inner.Id]), "The document is not at the index.");
        Require(batches.Single().Single() is { Type: ItemType.Folder, Kind: ChangeKind.Updated } change && change.Id == notes.Id,
            "A move inside the folder did not change only the folder's order.");
        var movedFolder = await c.Repository.MoveFolderAsync(inner.Id, notes.Id, -5, inner.ETag);
        var order = (await c.Repository.GetAsync<Folder>(notes.Id))!.ChildIds;
        Require(movedFolder.ETag == inner.ETag && order.SequenceEqual([inner.Id, b.Id, a.Id]) && SavedOrder(c.Root, "Notes").SequenceEqual(order),
            "A folder moved inside its folder is not first, or the order was not saved.");
        Require(batches.Count == 2 && batches[1].Single().Id == notes.Id, "A folder moved inside its folder changed more than the order.");
    }),
    ("Each write reports its changes once: a move is Moved, and a rename and the documents in a moved folder are Updated", async c =>
    {
        var batches = new List<IReadOnlyList<Change>>();
        c.Repository.Changed += (_, e) => batches.Add(e.Changes);
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var notes = await c.FolderAt("Notes");
        Change Only(int batch, string id) => batches[batch].Single(change => change.Id == id);

        scene = await c.Projects.RenameDocumentAsync(scene.Id, "Renamed", scene.ETag);
        Require(batches.Count == 1 && Only(0, scene.Id).Kind == ChangeKind.Updated && Only(0, scene.Id).ETag == scene.ETag, "A rename is not one Updated change.");
        batches.Clear();
        await c.Projects.MoveFolderToFolderAsync(chapter.Id, notes.Id, 0, chapter.ETag);
        Require(batches.Count == 1 && Only(0, chapter.Id).Kind == ChangeKind.Moved, "The moved folder is not one Moved change.");
        Require(Only(0, scene.Id).Kind == ChangeKind.Updated, "A document in the moved folder is not Updated.");
        batches.Clear();
        scene = await c.Projects.GetAsync<IDocument>(scene.Id);
        await c.Projects.MoveDocumentToFolderAsync(scene.Id, notes.Id, 0, scene.ETag);
        Require(batches.Count == 1 && Only(0, scene.Id).Kind == ChangeKind.Moved && Only(0, chapter.Id).Kind == ChangeKind.Updated
            && Only(0, notes.Id).Kind == ChangeKind.Updated, "The moved document is not one batch with the move and both folders.");
        batches.Clear();
        var folder = await c.Projects.CreateFolderAsync(notes.Id, "New");
        Require(batches.Count == 1 && Only(0, folder.Id).Kind == ChangeKind.Added && Only(0, folder.OwnDocumentId!).Kind == ChangeKind.Added
            && Only(0, notes.Id).Kind == ChangeKind.Updated, "A new folder is not one batch with the folder, its own document and its parent.");
        batches.Clear();
        await c.Projects.DeleteFolderAsync(folder.Id, folder.ETag);
        Require(Only(0, folder.Id).Kind == ChangeKind.Removed && Only(0, folder.OwnDocumentId!) is { Kind: ChangeKind.Removed, ETag: null },
            "A deleted folder and its own document are not Removed.");
    }),
    ("A folder that another program moves keeps its ID and the IDs and details of its documents", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Projects.UpdateDocumentDetailsAsync(scene.Id, new DocumentDetails("Kept title", "Kept", "", DocumentStatus.Done, 5), scene.ETag);
        Directory.Move(Path.Combine(c.Root, "Manuscript", "Chapter 01"), Path.Combine(c.Root, "Notes", "Moved chapter"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var moved = await c.FolderAt("Notes/Moved chapter");
        var document = await c.DocumentAt("Notes/Moved chapter/Scene 01.md");
        Require(moved.Id == chapter.Id, "The moved folder did not keep its ID.");
        Require(document.Id == scene.Id && document.Title == "Kept title" && document.Status == DocumentStatus.Done, "The documents in the moved folder did not keep their IDs and details.");
        Require(moved.ParentFolderId == (await c.FolderAt("Notes")).Id, "The moved folder has the wrong parent.");
    }),
    ("Text saves check the ETag, and keep front matter that another program wrote", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var file = Path.Combine(c.Root, "Manuscript", "Chapter 01", "Scene 01.md");
        await File.WriteAllTextAsync(file, "---\ntags: [draft]\n---\nOld text");
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        scene = await c.Projects.GetAsync<IDocument>(scene.Id);
        Require(await c.Projects.GetDocumentTextAsync(scene.Id) == "Old text", "The front matter was not split off the text.");
        var saved = await c.Projects.UpdateDocumentTextAsync(scene.Id, "New words here", scene.ETag);
        Require(saved.ETag != scene.ETag && saved.WordCount == 3, "The save did not give a new ETag and word count.");
        Require(await File.ReadAllTextAsync(file) == "---\ntags: [draft]\n---\nNew words here", "The front matter was not kept.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Projects.UpdateDocumentTextAsync(scene.Id, "Lost", scene.ETag));
        await Expect(WorkspaceError.ETagMismatch, () => c.Projects.UpdateDocumentTextAsync(scene.Id, "Lost", ""));
    }),
    ("A change to one item does not change the ETag of another item", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        var project = await c.Projects.GetAsync<IProject>(c.Project.Id);
        await c.Projects.UpdateDocumentTextAsync(scene.Id, "Changed", scene.ETag);
        await c.Projects.UpdateFolderLayoutAsync(notes.Id, new FolderLayout { PinnedView = "board" }, notes.ETag);
        Require((await c.Projects.GetAsync<IDocument>(style.Id)).ETag == style.ETag, "A change to one document changed another document's ETag.");
        Require((await c.Projects.GetAsync<IFolder>(threads.Id)).ETag == threads.ETag, "A change to one folder changed another folder's ETag.");
        Require((await c.Projects.GetAsync<IProject>(c.Project.Id)).ETag == project.ETag, "A change to a document or folder changed the project's ETag.");
        await c.Projects.UpdateDocumentTextAsync(style.Id, "Still works", style.ETag);
    }),
    ("Details replace all values, are saved in folder.json, and are read again from the disk", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var details = new DocumentDetails("Opening", "The start.", "Check dates.", DocumentStatus.Revised, 2500);
        scene = await c.Projects.UpdateDocumentDetailsAsync(scene.Id, details, scene.ETag);
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["documents"]![scene.Id]!;
        Require(entry["title"]!.GetValue<string>() == "Opening" && entry["status"]!.GetValue<string>() == "revised" && entry["wordGoal"]!.GetValue<int>() == 2500,
            "The details were not saved in folder.json.");
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var read = await c.Projects.GetAsync<IDocument>(scene.Id);
        Require(read.Title == "Opening" && read.Synopsis == "The start." && read.Status == DocumentStatus.Revised && read.ETag == scene.ETag,
            "The details were not read again from the disk.");
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateDocumentDetailsAsync(scene.Id, details with { Title = " " }, scene.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateDocumentDetailsAsync(scene.Id, details with { WordGoal = -1 }, scene.ETag));
    }),
    ("A rename changes the file name and keeps the ID, the details and the links", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var link = await c.Projects.CreateLinkAsync(scene.Id, style.Id, "Uses this style");
        var renamed = await c.Projects.RenameDocumentAsync(scene.Id, "The storm", scene.ETag);
        Require(renamed.Id == scene.Id && renamed.Name == "The storm.md" && renamed.Title == scene.Title, "The rename lost the ID or changed the title.");
        Require(File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 01", "The storm.md")) && !File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 01", "Scene 01.md")),
            "The file was not renamed on the disk.");
        Require((await c.Projects.GetLinksForDocumentAsync(scene.Id)).Single().Id == link.Id, "The link was lost.");
        var second = await c.Projects.CreateDocumentAsync(renamed.FolderId, "Second");
        await Expect(WorkspaceError.Conflict, () => c.Projects.RenameDocumentAsync(second.Id, "The storm", second.ETag));
        var own = await c.Projects.GetAsync<IDocument>((await c.FolderAt("Manuscript")).OwnDocumentId!);
        await Expect(WorkspaceError.Invalid, () => c.Projects.RenameDocumentAsync(own.Id, "Other", own.ETag));
    }),
    ("A document moves between folders with its file, its details and a new kind", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Projects.UpdateDocumentDetailsAsync(scene.Id, new DocumentDetails("Kept", "", "", DocumentStatus.Draft, 10), scene.ETag);
        var characters = await c.FolderAt("Characters");
        var result = await c.Projects.MoveDocumentToFolderAsync(scene.Id, characters.Id, 0, scene.ETag);
        Require(result.Document.FolderId == characters.Id && result.Document.Kind == DocumentKind.Character && result.Document.Title == "Kept",
            "The moved document has the wrong folder, kind or details.");
        Require(result.NewFolder.ChildIds[0] == scene.Id && !result.OldFolder.ChildIds.Contains(scene.Id), "The folders' orders were not updated.");
        Require(File.Exists(Path.Combine(c.Root, "Characters", "Scene 01.md")), "The file did not move.");
        Require(await c.PathOf(scene.Id) == "Characters/Scene 01.md", "The document's path was not changed.");
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var own = await c.Projects.GetAsync<IDocument>(chapter.OwnDocumentId!);
        await Expect(WorkspaceError.Invalid, () => c.Projects.MoveDocumentToFolderAsync(own.Id, characters.Id, 0, own.ETag));
        var other = await c.Projects.CreateDocumentAsync(chapter.Id, "Scene 01");
        await Expect(WorkspaceError.Conflict, () => c.Projects.MoveDocumentToFolderAsync(other.Id, characters.Id, 0, other.ETag));
    }),
    ("The order of a folder's children changes only by a move", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var a = await c.Projects.CreateDocumentAsync(chapter.Id, "A");
        var b = await c.Projects.CreateDocumentAsync(chapter.Id, "B");
        chapter = await c.Projects.GetAsync<IFolder>(chapter.Id);
        Require(chapter.ChildIds.TakeLast(2).SequenceEqual([a.Id, b.Id]), "New documents are not at the end.");
        var moved = await c.Projects.MoveDocumentToFolderAsync(b.Id, chapter.Id, 0, b.ETag);
        Require(moved.NewFolder.ChildIds[0] == b.Id && moved.OldFolder.Id == moved.NewFolder.Id, "The reorder did not put the document first.");
        var order = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["itemOrder"]!.AsArray();
        Require(order[0]!.GetValue<string>() == b.Id, "The order was not saved in folder.json.");
        var inOrder = (await c.Projects.GetChildrenAsync(chapter.Id)).Select(document => document.Id).ToArray();
        Require(inOrder[0] == b.Id, "The documents of the folder are not in order.");
    }),
    ("A folder moves with everything inside it, and cannot move into itself", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var notes = await c.FolderAt("Notes");
        var result = await c.Projects.MoveFolderToFolderAsync(chapter.Id, notes.Id, 0, chapter.ETag);
        Require(result.Folder.Id == chapter.Id && result.Folder.ParentFolderId == notes.Id && result.NewParentFolder.ChildIds[0] == chapter.Id,
            "The folder did not move into the target folder.");
        Require(Directory.Exists(Path.Combine(c.Root, "Notes", "Chapter 01")), "The folder did not move on the disk.");
        var movedScene = await c.Projects.GetAsync<IDocument>(scene.Id);
        Require(movedScene.Kind == DocumentKind.Note && await c.PathOf(scene.Id) == "Notes/Chapter 01/Scene 01.md",
            "The documents inside the folder did not get their new path and kind.");
        var inner = await c.Projects.CreateFolderAsync(chapter.Id, "Inner");
        var moved = await c.Projects.GetAsync<IFolder>(chapter.Id);
        await Expect(WorkspaceError.Invalid, () => c.Projects.MoveFolderToFolderAsync(chapter.Id, inner.Id, 0, moved.ETag));
        var root = await c.Projects.GetAsync<IFolder>(c.Project.RootFolderId);
        await Expect(WorkspaceError.Invalid, () => c.Projects.MoveFolderToFolderAsync(root.Id, notes.Id, 0, root.ETag));
    }),
    ("A folder is deleted only when it is empty, and view settings that name it are cleared", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var empty = await c.Projects.CreateFolderAsync(notes.Id, "Empty");
        var root = await c.Projects.GetAsync<IFolder>(c.Project.RootFolderId);
        await c.Projects.UpdateFolderLayoutAsync(root.Id, Layout("grid", empty.Id), root.ETag);
        var full = await c.Projects.CreateFolderAsync(notes.Id, "Full");
        await c.Projects.CreateDocumentAsync(full.Id, "Inside");
        full = await c.Projects.GetAsync<IFolder>(full.Id);
        await Expect(WorkspaceError.Conflict, () => c.Projects.DeleteFolderAsync(full.Id, full.ETag));
        await c.Projects.DeleteFolderAsync(empty.Id, empty.ETag);
        Require(!Directory.Exists(Path.Combine(c.Root, "Notes", "Empty")), "The empty folder is still on the disk.");
        Require(!(await c.Projects.GetAsync<IFolder>(notes.Id)).ChildIds.Contains(empty.Id), "The deleted folder is still a child.");
        Require(ColumnFolder(await c.Projects.GetAsync<IFolder>(root.Id)) is null, "The view setting that named the deleted folder was not cleared.");
        await Expect(WorkspaceError.NotFound, () => c.Projects.GetAsync<IFolder>(empty.Id));
        var threads = await c.FolderAt("Threads");
        await Expect(WorkspaceError.Forbidden, () => c.Projects.DeleteFolderAsync(threads.Id, threads.ETag));
    }),
    ("Folders keep any view name and settings, and a folder setting must name a folder of the project", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        notes = await c.Projects.UpdateFolderLayoutAsync(notes.Id, new FolderLayout
        {
            PinnedView = "tree", Views = new Dictionary<string, JsonElement> { ["tree"] = Json("""{"depth":3}""") },
        }, notes.ETag);
        notes = await c.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("tree", threads.Id), notes.ETag);
        Require(notes.PinnedView == "tree" && notes.Views["tree"].GetProperty("depth").GetInt32() == 3 && ColumnFolder(notes) == threads.Id,
            "A view the server does not know lost its settings.");
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("grid", Guid.NewGuid().ToString()), notes.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateFolderLayoutAsync(notes.Id,
            new FolderLayout { Views = new Dictionary<string, JsonElement> { ["tree"] = Json("5") } }, notes.ETag));
        notes = await c.Projects.UpdateFolderLayoutAsync(notes.Id, new FolderLayout
        {
            PinnedView = null, Views = new Dictionary<string, JsonElement> { ["tree"] = Json("null") },
        }, notes.ETag);
        Require(notes.PinnedView is null && !notes.Views.ContainsKey("tree") && notes.Views.ContainsKey("grid"), "A JSON null did not remove only that view's settings.");
    }),
    ("Links are saved once, have no direction, and come back when their document comes back from a version", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var link = await c.Projects.CreateLinkAsync(scene.Id, style.Id, " A note ");
        Require(link.Note == "A note", "The note was not trimmed.");
        await Expect(WorkspaceError.Conflict, () => c.Projects.CreateLinkAsync(style.Id, scene.Id));
        await Expect(WorkspaceError.Invalid, () => c.Projects.CreateLinkAsync(scene.Id, scene.Id));
        link = await c.Projects.UpdateLinkNoteAsync(link.Id, "Changed", link.ETag);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "links.json")))!["links"]!.AsArray();
        Require(file.Count == 1 && file[0]!["note"]!.GetValue<string>() == "Changed", "The link is not saved once in links.json.");
        var version = await c.History.SaveVersionAsync(c.Project.Id, "With the style sheet");
        File.Delete(Path.Combine(c.Root, "Styles", "Default.md"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        Require((await c.Projects.GetAllAsync<ILink>(c.Project.Id)).Count == 0, "A link to a deleted document is still listed.");
        await c.History.RestoreProjectVersionAsync(c.Project.Id, version.Id);
        var restored = (await c.Projects.GetAllAsync<ILink>(c.Project.Id)).SingleOrDefault();
        Require(restored?.Id == link.Id && restored.Note == "Changed", "The link did not come back with its document.");
        await c.Projects.DeleteLinkAsync(restored!.Id, restored.ETag);
        Require((await c.Projects.GetLinksForDocumentAsync(scene.Id)).Count == 0, "The link was not deleted.");
    }),
    ("Files and folders that other programs add, change, rename or copy are read correctly", async c =>
    {
        var characters = await c.FolderAt("Characters");
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Characters", "Outside.md"), "Made outside.");
        Directory.CreateDirectory(Path.Combine(c.Root, "Notes", "Outside folder"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var outside = await c.DocumentAt("Characters/Outside.md");
        Require(outside.Kind == DocumentKind.Character && outside.Title == "Outside", "A file added outside did not become a document.");
        Require((await c.Projects.GetAsync<IFolder>(characters.Id)).ChildIds[^1] == outside.Id, "The added document is not at the end of its folder.");
        var added = await c.FolderAt("Notes/Outside folder");
        Require(added.OwnDocumentId is not null && File.Exists(Path.Combine(c.Root, "Notes", "Outside folder", ".odysseum", "folder.json")),
            "A folder added outside did not get its settings file and own document.");

        await File.AppendAllTextAsync(Path.Combine(c.Root, "Characters", "Outside.md"), " More.");
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        Require((await c.Projects.GetAsync<IDocument>(outside.Id)).ETag != outside.ETag, "A change outside did not change the ETag.");

        File.Move(Path.Combine(c.Root, "Characters", "Outside.md"), Path.Combine(c.Root, "Characters", "Renamed.md"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        await Expect(WorkspaceError.NotFound, () => c.Projects.GetAsync<IDocument>(outside.Id));
        Require((await c.DocumentAt("Characters/Renamed.md")).Id != outside.Id, "A file renamed outside kept its old ID.");

        CopyFolder(Path.Combine(c.Root, "Characters"), Path.Combine(c.Root, "Characters copy"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var copy = await c.FolderAt("Characters copy");
        var renamed = await c.DocumentAt("Characters/Renamed.md");
        var copied = await c.DocumentAt("Characters copy/Renamed.md");
        Require(copy.Id != characters.Id && copied.Id != renamed.Id, "A copied folder or document kept the ID of the original.");
    }),
    ("A settings file that is not valid stops the project from opening", async c =>
    {
        var path = Path.Combine(c.Root, "Notes", ".odysseum", "folder.json");
        await File.WriteAllTextAsync(path, "{ not json");
        await Expect(WorkspaceError.Corrupt, () => c.Storage.ReloadProjectAsync(c.Project.Id));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), version = 1, itemOrder = Array.Empty<string>(), documents = new { } }));
        await Expect(WorkspaceError.Corrupt, () => c.Storage.ReloadProjectAsync(c.Project.Id));
    }),
    ("A second server cannot open a project that is open", async c =>
    {
        await using var second = await Workspace.StartAsync(Path.GetDirectoryName(c.Root)!);
        Require((await second.Projects.GetProjectsAsync()).All(project => project.Id != c.Project.Id), "A second server opened a project that is open.");
    }),
    ("Versions are saved, listed, read and restored for the project and for one document", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Projects.UpdateDocumentTextAsync(scene.Id, "First version", scene.ETag);
        var version = await c.History.SaveVersionAsync(c.Project.Id, "Checkpoint");
        scene = await c.Projects.UpdateDocumentTextAsync(scene.Id, "Second version", scene.ETag);
        await c.History.SaveVersionAsync(c.Project.Id, "Later");
        Require((await c.History.GetVersionsByProjectIdAsync(c.Project.Id)).Any(item => item.Name == "Checkpoint"), "The named version is not listed.");
        Require((await c.History.GetVersionsByDocumentIdAsync(scene.Id)).Count >= 2, "The document's versions are not listed.");
        Require(await c.History.GetDocumentTextFromVersionAsync(scene.Id, version.Id) == "First version", "The text from the version is wrong.");
        await c.History.RestoreDocumentVersionAsync(scene.Id, version.Id);
        Require(await c.Projects.GetDocumentTextAsync(scene.Id) == "First version", "The document was not restored.");
        await c.Projects.UpdateDocumentTextAsync(scene.Id, "Third", (await c.Projects.GetAsync<IDocument>(scene.Id)).ETag);
        await c.Projects.CreateFolderAsync((await c.FolderAt("Notes")).Id, "After the version");
        await c.History.RestoreProjectVersionAsync(c.Project.Id, version.Id);
        Require(await c.Projects.GetDocumentTextAsync(scene.Id) == "First version", "The project was not restored.");
        Require((await c.Projects.GetAllAsync<IFolder>(c.Project.Id)).All(folder => folder.Name != "After the version"),
            "A folder made after the version is still there after the restore.");
        await Expect(WorkspaceError.Invalid, () => c.History.SaveVersionAsync(c.Project.Id, " "));
    }),
    ("Search finds text, titles and notes, and export writes the scenes in manuscript order", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var first = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        await c.Projects.UpdateDocumentTextAsync(first.Id, "The lighthouse stood alone.", first.ETag);
        var second = await c.Projects.CreateDocumentAsync(chapter.Id, "Second scene", "Waves.");
        second = await c.Projects.UpdateDocumentDetailsAsync(second.Id, new DocumentDetails("Second scene", "", "Mentions a lighthouse", DocumentStatus.Draft, 0), second.ETag);
        var results = await c.Projects.SearchDocumentsAsync(c.Project.Id, "LIGHTHOUSE");
        Require(results.Select(result => result.Document.Id).SequenceEqual([first.Id, second.Id]), "Search did not find both documents in order.");
        Require(results[0].Excerpt.Contains("lighthouse"), "The excerpt does not show the match.");
        var export = await c.Projects.ExportMarkdownAsync(c.Project.Id);
        Require(export.StartsWith("# Test") && export.IndexOf("The lighthouse", StringComparison.Ordinal) < export.IndexOf("Waves.", StringComparison.Ordinal)
            && !export.Contains(".normal"), "The export is not the scenes in manuscript order.");
    }),
    ("A project saved as a template makes a project with the same folders, documents and layouts", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        await c.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("grid", threads.Id), notes.ETag);
        await c.Projects.CreateDocumentAsync(notes.Id, "Idea");
        await c.Projects.SaveAsTemplateAsync(c.Project.Id, "Mine");
        var template = c.Workspace.TemplateFiles.Get("Mine");
        Require(template.Folders.Single(folder => folder.Path == "Notes").Views!["grid"].GetProperty("columnFolder").GetString() == "Threads",
            "The template does not name the column folder by path.");
        var made = await c.Projects.CreateProjectAsync("From mine", templateName: "Mine");
        var madeNotes = (await c.Projects.GetAllAsync<IFolder>(made.Id)).Single(folder => folder.Name == "Notes" && folder.ParentFolderId == made.RootFolderId);
        var madeThreads = (await c.Projects.GetAllAsync<IFolder>(made.Id)).Single(folder => folder.Name == "Threads" && folder.ParentFolderId == made.RootFolderId);
        Require(madeNotes.PinnedView == "grid" && ColumnFolder(madeNotes) == madeThreads.Id, "The layout was not made in the new project.");
        Require((await c.Projects.GetChildrenAsync(madeNotes.Id)).Any(document => document.Title == "Idea"), "The document was not made in the new project.");
        await Expect(WorkspaceError.NotFound, () => c.Projects.CreateProjectAsync("Missing", templateName: "No such template"));
    }),
    ("Project settings replace all values and check them", async c =>
    {
        var project = await c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("Renamed", 90000, 1500), c.Project.ETag);
        Require(project.Title == "Renamed" && project.WordGoal == 90000 && project.DefaultSceneWordGoal == 1500, "The settings were not saved.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("Old", 1, 1), c.Project.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("", 1, 1), project.ETag));
        var created = await c.Projects.CreateDocumentAsync((await c.FolderAt("Manuscript")).Id, "New");
        Require(created.WordGoal == 1500, "A new document does not get the project's default word goal.");
    }),
    ("Service rules: a new folder goes last, a move goes to the index, a full folder stays, and an own document keeps its name", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var first = await c.Projects.CreateDocumentAsync(notes.Id, "First");
        var folder = await c.Projects.CreateFolderAsync(notes.Id, "Ideas");
        notes = await c.Projects.GetAsync<IFolder>(notes.Id);
        Require(notes.ChildIds.SequenceEqual([first.Id, folder.Id]) && folder.ParentFolderId == notes.Id && folder.Name == "Ideas",
            "The new folder is not the last child of its parent.");
        Require(folder.OwnDocumentId is { } ownId && (await c.Projects.GetAsync<IDocument>(ownId)).IsFolderDocument
            && File.Exists(Path.Combine(c.Root, "Notes", "Ideas", ".Ideas.md")), "The new folder has no own document.");

        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var result = await c.Projects.MoveDocumentToFolderAsync(scene.Id, notes.Id, 1, scene.ETag);
        Require(result.NewFolder.ChildIds.SequenceEqual([first.Id, scene.Id, folder.Id]) && !result.OldFolder.ChildIds.Contains(scene.Id),
            "The document is not at the index.");
        var folderMove = await c.Projects.MoveFolderToFolderAsync(folder.Id, notes.Id, 0, folder.ETag);
        Require(folderMove.NewParentFolder.ChildIds.SequenceEqual([folder.Id, first.Id, scene.Id]), "The folder is not at the index.");

        await c.Projects.CreateDocumentAsync(folder.Id, "Inside");
        folder = await c.Projects.GetAsync<IFolder>(folder.Id);
        await Expect(WorkspaceError.Conflict, () => c.Projects.DeleteFolderAsync(folder.Id, folder.ETag));
        Require(Directory.Exists(Path.Combine(c.Root, "Notes", "Ideas")), "A folder with a document in it was deleted.");

        var own = await c.Projects.GetAsync<IDocument>(folder.OwnDocumentId!);
        await Expect(WorkspaceError.Invalid, () => c.Projects.RenameDocumentAsync(own.Id, "Other", own.ETag));
        Require(File.Exists(Path.Combine(c.Root, "Notes", "Ideas", ".Ideas.md")), "The folder's own document was renamed.");
    }),
    ("With the views plugin loaded, a grid columnFolder must name a folder of the project", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("grid", Guid.NewGuid().ToString()), notes.ETag));
        notes = await c.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("grid", threads.Id), notes.ETag);
        Require(ColumnFolder(notes) == threads.Id, "A columnFolder that names a folder of the project was not saved.");
    }),
    ("Names that are not allowed are refused", async c =>
    {
        var notes = await c.FolderAt("Notes");
        await Expect(WorkspaceError.Invalid, () => c.Projects.CreateFolderAsync(notes.Id, "a/b"));
        await Expect(WorkspaceError.Invalid, () => c.Projects.CreateFolderAsync(notes.Id, ".hidden"));
        await Expect(WorkspaceError.Invalid, () => c.Projects.CreateDocumentAsync(notes.Id, "  "));
        await c.Projects.CreateFolderAsync(notes.Id, "Twice");
        await Expect(WorkspaceError.Conflict, () => c.Projects.CreateFolderAsync(notes.Id, "twice"));
        var first = await c.Projects.CreateDocumentAsync(notes.Id, "Same");
        var second = await c.Projects.CreateDocumentAsync(notes.Id, "Same");
        Require(first.Name == "Same.md" && second.Name == "Same-2.md", "A second document with the same title did not get a free file name.");
    }),
};

var libraryChecks = new List<(string Name, Func<string, Task> Run)>
{
    ("The views plugin loads from its manifest in its own load context, and board, outline and grid are known", root =>
    {
        var plugin = TestPlugins.Loaded.SingleOrDefault(plugin => plugin.Id == "views")
            ?? throw new Exception($"The views plugin did not load from {TestPlugins.Folder}.");
        Require(plugin is { Status: PluginStatus.Enabled, Error: null, Manifest: { Name: "Default views", Version: "1.0.0", Assembly: "Odysseum.Plugins.Views.dll" } },
            "The views plugin is not enabled, or its manifest was not read.");
        Require(plugin.Views.Select(view => view.Name).SequenceEqual(["board", "outline", "grid"]), "The views plugin has the wrong views.");
        Require(TestPlugins.Views.Views.Select(view => view.Name).Order().SequenceEqual(["board", "grid", "outline", "write"]),
            "The known views are not write and the plugin's views.");
        Require(TestPlugins.Views.Views.Single(view => view.Name == "grid").FolderSettings.SequenceEqual(["columnFolder"]),
            "The grid view has no columnFolder folder setting.");
        Require(plugin.ClientEntryUrl == "/plugins/views/index.js", "The client entry is wrong.");
        Require(typeof(ProjectService).Assembly.GetReferencedAssemblies().All(name => name.Name != "Odysseum.Plugins.Views")
            && AssemblyLoadContext.Default.Assemblies.All(assembly => assembly.GetName().Name != "Odysseum.Plugins.Views"),
            "The server references the views plugin, or the plugin is in the server's load context.");
        Require(!File.Exists(Path.Combine(plugin.Folder, "Odysseum.Abstractions.dll")), "The plugin ships its own copy of Odysseum.Abstractions.");
        return Task.CompletedTask;
    }),
    ("A plugin folder without a valid manifest is skipped, a second plugin with the same id too, and a broken plugin fails", root =>
    {
        var source = TestPlugins.Loaded.Single(plugin => plugin.Id == "views").Folder;
        void Copy(string name, string? manifest)
        {
            CopyFolder(source, Path.Combine(root, name));
            if (manifest is null) File.Delete(Path.Combine(root, name, "plugin.json"));
            else File.WriteAllText(Path.Combine(root, name, "plugin.json"), manifest);
        }
        const string Valid = """{ "id": "views", "name": "Default views", "version": "1.0.0", "assembly": "Odysseum.Plugins.Views.dll", "clientEntry": "index.js" }""";
        Copy("a-valid", Valid);
        Copy("b-no-manifest", null);
        Copy("c-not-json", "{ not json");
        Copy("d-no-assembly", Valid.Replace("Odysseum.Plugins.Views.dll", "Missing.dll").Replace("\"views\"", "\"other\""));
        Copy("e-escaping-entry", Valid.Replace("index.js", "../plugin.json").Replace("\"views\"", "\"escape\""));
        Copy("f-bad-id", Valid.Replace("\"views\"", "\"Bad Id\""));
        Copy("g-same-id", Valid);
        MakeBrokenPlugin(Path.Combine(root, "h-broken"), "broken");
        var loaded = new PluginLoader().Load(root, []);
        Require(loaded.Count == 2 && loaded[0] is { Id: "views", Status: PluginStatus.Enabled } && Path.GetFileName(loaded[0].Folder) == "a-valid",
            "A folder without a valid manifest, or a second plugin with the same id, was not skipped.");
        Require(loaded[1] is { Id: "broken", Status: PluginStatus.Failed, Views.Count: 0, ClientEntryUrl: null }
            && loaded[1].Error == "The plugin's assembly could not be loaded.",
            "A plugin whose assembly cannot load is not listed as failed with an error.");
        return Task.CompletedTask;
    }),
    ("With the views plugin disabled, only write is known, and grid settings stay unchanged", async root =>
    {
        var installed = new PluginLoader().Load(TestPlugins.Folder, ["views"]);
        Require(installed.Single(plugin => plugin.Id == "views") is { Status: PluginStatus.Disabled, Error: null, Views.Count: 0, ClientEntryUrl: null },
            "The disabled plugin is not listed as disabled, or it has views or a client entry.");
        var views = new ViewCatalog(installed.SelectMany(plugin => plugin.Views));
        Require(views.Views.Select(view => view.Name).SequenceEqual(["write"]), "A view other than write is known without the plugin.");
        IProject project;
        string notesId, columnsId, saved;
        await using (var w = await Workspace.StartAsync(root))
        {
            project = await w.Projects.CreateProjectAsync("Disabled");
            var notes = (await w.Projects.GetAllAsync<IFolder>(project.Id)).Single(folder => folder.Name == "Notes");
            var columns = await w.Projects.CreateFolderAsync(notes.Id, "Columns");
            notes = await w.Projects.GetAsync<IFolder>(notes.Id);
            notes = await w.Projects.UpdateFolderLayoutAsync(notes.Id, Layout("grid", columns.Id), notes.ETag);
            (notesId, columnsId, saved) = (notes.Id, columns.Id, notes.Views["grid"].GetRawText());
        }
        await using (var w = await Workspace.StartAsync(root, views: views))
        {
            var notes = await w.Projects.GetAsync<IFolder>(notesId);
            Require(SameJson(notes.Views["grid"], saved), "The grid settings changed when the project was read without the plugin.");
            notes = await w.Projects.UpdateFolderLayoutAsync(notesId, new FolderLayout { PinnedView = "write" }, notes.ETag);
            await w.Projects.DeleteFolderAsync(columnsId, (await w.Projects.GetAsync<IFolder>(columnsId)).ETag);
            notes = await w.Projects.GetAsync<IFolder>(notesId);
            Require(notes.PinnedView == "write" && SameJson(notes.Views["grid"], saved),
                "The grid settings changed after a layout change and a folder delete without the plugin.");
            var onDisk = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, project.Name, "Notes", ".odysseum", "folder.json")))!["views"]!["grid"]!;
            Require(onDisk["columnFolder"]!.GetValue<string>() == columnsId, "The grid settings on the disk changed without the plugin.");
        }
    }),
    ("GET /api/plugins gives each plugin's status: enabled, disabled or failed with an error", async root =>
    {
        await WithServerAsync(Path.Combine(root, "enabled"), [], async http =>
        {
            var plugin = await PluginFromApiAsync(http);
            Require(plugin["name"]!.GetValue<string>() == "Default views" && plugin["version"]!.GetValue<string>() == "1.0.0"
                && plugin["status"]!.GetValue<string>() == "enabled" && plugin["clientEntry"]!.GetValue<string>() == "/plugins/views/index.js",
                "GET /api/plugins gives the wrong name, version, status or client entry.");
            Require(!plugin.AsObject().ContainsKey("error"), "An enabled plugin has an error field.");
            var module = await http.GetAsync("/plugins/views/index.js");
            Require(module.IsSuccessStatusCode && module.Content.Headers.ContentType?.MediaType?.Contains("javascript") == true,
                "The client entry is not served as JavaScript.");
        });
        var plugins = Path.Combine(root, "plugins");
        CopyFolder(TestPlugins.Loaded.Single(plugin => plugin.Id == "views").Folder, Path.Combine(plugins, "views"));
        MakeBrokenPlugin(Path.Combine(plugins, "broken"), "broken");
        await WithServerAsync(Path.Combine(root, "disabled"), new() { ["ODYSSEUM_PLUGINS"] = plugins, ["ODYSSEUM_DISABLED_PLUGINS"] = "views" }, async http =>
        {
            var disabled = await PluginFromApiAsync(http);
            Require(disabled["status"]!.GetValue<string>() == "disabled" && !disabled.AsObject().ContainsKey("error") && disabled["clientEntry"] is null,
                "A disabled plugin is not listed as disabled, or has an error or a client entry.");
            Require((await http.GetAsync("/plugins/views/index.js")).StatusCode == HttpStatusCode.NotFound, "A disabled plugin's files are served.");
            var failed = await PluginFromApiAsync(http, "broken");
            Require(failed["status"]!.GetValue<string>() == "failed"
                && failed["error"]?.GetValue<string>() == "The plugin's assembly could not be loaded.",
                "A plugin that cannot load is not listed as failed with an error.");
        });
    }),
    ("The watcher reports changes from other programs, but not the server's own writes", async root =>
    {
        await using var w = await Workspace.StartAsync(root, watch: true);
        var project = await w.Projects.CreateProjectAsync("Watched");
        var scene = (await w.Projects.GetDocumentsInOrderAsync(project.Id)).Single(document => document.Name == "Scene 01.md");
        var updates = 0;
        w.Projects.Changed += (_, e) =>
        {
            if (e.Changes.Any(change => change is { Type: ItemType.Document, Kind: ChangeKind.Updated } && change.Id == scene.Id)) Interlocked.Increment(ref updates);
        };
        scene = await w.Projects.UpdateDocumentTextAsync(scene.Id, "Typed in the app", scene.ETag);
        await Task.Delay(1500);
        Require(Volatile.Read(ref updates) == 1, "The server's own write was reported more than once.");
        await File.AppendAllTextAsync(Path.Combine(root, project.Name, "Manuscript", "Chapter 01", "Scene 01.md"), "\nTyped outside.");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await w.Projects.GetAsync<IDocument>(scene.Id)).ETag == scene.ETag && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require((await w.Projects.GetAsync<IDocument>(scene.Id)).ETag != scene.ETag, "An edit made outside was not read.");
        Require(Volatile.Read(ref updates) == 2, "An edit made outside did not send one update event.");
    }),
};

foreach (var (name, run) in checks)
{
    var root = Path.Combine(testRoot, "check-" + passed);
    await using var workspace = await Workspace.StartAsync(root, settings: new DefaultServerSettings());
    var project = await workspace.Projects.CreateProjectAsync("Test");
    await run(new Check(Path.Combine(root, project.Name), project, workspace));
    passed++;
    Console.WriteLine($"PASS {name}");
}
foreach (var (name, run) in libraryChecks)
{
    await run(Path.Combine(testRoot, "library-" + passed));
    passed++;
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"\n{passed} storage checks passed. Fixtures: {testRoot}");

static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
static FolderLayout Layout(string? pinnedView, string? columnFolder = null) => new()
{
    PinnedView = pinnedView,
    Views = columnFolder is null ? null : new Dictionary<string, JsonElement> { ["grid"] = JsonSerializer.SerializeToElement(new { columnFolder }) },
};
static string? ColumnFolder(IFolder folder) =>
    folder.Views.TryGetValue("grid", out var grid) && grid.TryGetProperty("columnFolder", out var id) ? id.GetString() : null;
static IEnumerable<string> SavedOrder(string root, string folderPath) =>
    JsonNode.Parse(File.ReadAllText(Path.Combine(root, folderPath, ".odysseum", "folder.json")))!["itemOrder"]!.AsArray().Select(id => id!.GetValue<string>());
static void CopyFolder(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    foreach (var folder in Directory.EnumerateDirectories(source)) CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
}
// Starts the built server as its own process on a free port, with a scratch folder for everything it keeps, runs the
// test, and stops the server.
static async Task WithServerAsync(string root, Dictionary<string, string> environment, Func<HttpClient, Task> test)
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    Directory.CreateDirectory(root);
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(TestPlugins.ServerOutput, "Odysseum.Server.dll"));
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["ODYSSEUM_SETTINGS"] = Path.Combine(root, "server-settings.json");
    start.Environment["ODYSSEUM_WORKSPACE"] = Path.Combine(root, "workspace");
    start.Environment["ODYSSEUM_WEBUI"] = Path.Combine(root, "webui");
    start.Environment["ODYSSEUM_THEMES"] = Path.Combine(root, "themes");
    start.Environment["ODYSSEUM_TEMPLATES"] = Path.Combine(root, "templates");
    start.Environment["ODYSSEUM_KEYS"] = Path.Combine(root, "keys");
    start.Environment["ODYSSEUM_DEMO"] = "false";
    start.Environment["ODYSSEUM_PLUGINS"] = TestPlugins.Folder;
    foreach (var (name, value) in environment) start.Environment[name] = value;
    var log = new StringBuilder();
    using var server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.BeginOutputReadLine();
    server.BeginErrorReadLine();
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        for (var deadline = DateTime.UtcNow.AddSeconds(60); ; await Task.Delay(200))
        {
            try { if ((await http.GetAsync("/health")).IsSuccessStatusCode) break; }
            catch (HttpRequestException) { }
            if (server.HasExited || DateTime.UtcNow > deadline) lock (log) throw new Exception("The server did not start:\n" + log);
        }
        await test(http);
    }
    finally
    {
        server.Kill(entireProcessTree: true);
        await server.WaitForExitAsync();
    }
}
static async Task<JsonNode> PluginFromApiAsync(HttpClient http, string id = "views") =>
    JsonNode.Parse(await http.GetStringAsync("/api/plugins"))!["data"]!["items"]!.AsArray()
        .SingleOrDefault(item => item!["id"]!.GetValue<string>() == id) ?? throw new Exception($"GET /api/plugins does not list the {id} plugin.");
// A plugin with a valid manifest whose assembly is not a .NET assembly, so loading it fails.
static void MakeBrokenPlugin(string folder, string id)
{
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""{ "id": "{{id}}", "name": "Broken", "version": "1.0.0", "assembly": "Broken.dll" }""");
    File.WriteAllText(Path.Combine(folder, "Broken.dll"), "This is not an assembly.");
}
static bool SameJson(JsonElement element, string json) => JsonNode.DeepEquals(JsonNode.Parse(element.GetRawText()), JsonNode.Parse(json));
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static async Task Expect(WorkspaceError error, Func<Task> action)
{
    try { await action(); }
    catch (WorkspaceException ex) when (ex.Error == error) { return; }
    throw new Exception($"Expected a {error} rejection.");
}

/// <summary>The server's storage, repositories and services on one workspace folder, without the web part.</summary>
sealed class Workspace : IAsyncDisposable
{
    private Workspace(string root, ISettingsProvider? settings, bool watch, ViewCatalog views)
    {
        OwnWrites = new OwnWrites();
        Watcher = watch ? new FileProjectWatcher(root, OwnWrites, 300) : new NullProjectWatcher();
        History = new GitProjectHistory(root);
        Storage = new DiskStorageContext(root, Watcher, OwnWrites);
        Repository = new WorkspaceRepository(Storage);
        TemplateFiles = new TemplateRepository(Path.Combine(root, "..", Path.GetFileName(root) + "-templates"));
        Projects = new ProjectService(Repository, Storage, TemplateFiles, settings, views);
        HistoryService = new HistoryService(Repository, History, Storage, 60);
    }

    /// <summary>Makes the workspace and reads its projects. Without <paramref name="views"/>, the views come from the
    /// plugins that <see cref="TestPlugins"/> loaded.</summary>
    public static async Task<Workspace> StartAsync(string root, ISettingsProvider? settings = null, bool watch = false, ViewCatalog? views = null)
    {
        var workspace = new Workspace(root, settings, watch, views ?? TestPlugins.Views);
        await workspace.Storage.LoadAllProjectsAsync();
        return workspace;
    }

    public OwnWrites OwnWrites { get; }
    public IProjectWatcher Watcher { get; }
    public GitProjectHistory History { get; }
    public DiskStorageContext Storage { get; }
    public WorkspaceRepository Repository { get; }
    public TemplateRepository TemplateFiles { get; }
    public IProjectService Projects { get; }
    public HistoryService HistoryService { get; }

    public async ValueTask DisposeAsync()
    {
        await HistoryService.DisposeAsync();
        await Storage.DisposeAsync();
        await Watcher.DisposeAsync();
        History.Dispose();
    }
}

/// <summary>The plugins in the plugins folder next to the server's build output, loaded once, as the server loads them.</summary>
static class TestPlugins
{
    public static string ServerOutput { get; } = Path.GetFullPath(typeof(TestPlugins).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "ServerOutput").Value!);
    public static string Folder { get; } = Path.Combine(ServerOutput, "plugins");
    public static IReadOnlyList<InstalledPlugin> Loaded { get; } = new PluginLoader().Load(Folder, []);
    public static ViewCatalog Views { get; } = new(Loaded.SelectMany(plugin => plugin.Views));
}

sealed class NullProjectWatcher : IProjectWatcher
{
    public event Action<string>? Changed { add { } remove { } }
    public void Watch(string projectName) { }
    public void Unwatch(string projectName) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>One check's project, with shortcuts to find its folders and documents by path.</summary>
sealed class Check(string root, IProject project, Workspace workspace)
{
    public string Root => root;
    public IProject Project => project;
    public Workspace Workspace => workspace;
    public DiskStorageContext Storage => workspace.Storage;
    public IProjectService Projects => workspace.Projects;
    public HistoryService History => workspace.HistoryService;
    public WorkspaceRepository Repository => workspace.Repository;

    public async Task<string> PathOf(string documentId) => (await Repository.GetAsync<Document>(documentId))!.Path;

    public async Task<IFolder> FolderAt(string path) =>
        (await Repository.GetAllAsync<Folder>(project.Id)).SingleOrDefault(folder => folder.Path == path)
            ?? throw new Exception($"There is no folder at '{path}'.");

    public async Task<IDocument> DocumentAt(string path) =>
        (await Repository.GetAllAsync<Document>(project.Id)).SingleOrDefault(document => document.Path == path)
            ?? throw new Exception($"There is no document at '{path}'.");
}

/// <summary>Server settings with their default values: default project folders cannot be deleted.</summary>
sealed class DefaultServerSettings : ISettingsProvider
{
    public IServerSettings GetSettings(bool copy = false) => new ServerSettings();
    public void SaveSettings(IServerSettings settings) { }
    public void SaveSettings() { }
    public void DebugSettingsToLog() { }
}
