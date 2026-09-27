using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Disk;
using Odysseum.Server.Repositories.Git;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<Check, Task> Run)>
{
    ("A new project gets the Default template's folders, each with its own hidden document", async c =>
    {
        var root = await c.Folders.GetFolderByIdAsync(c.Project.RootFolderId);
        var names = new List<string>();
        foreach (var id in root.ChildIds) names.Add((await c.Folders.GetFolderByIdAsync(id)).Name);
        Require(names.SequenceEqual(["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"]), "The top folders are not the default folders in order.");
        foreach (var folder in await c.Folders.GetFoldersByProjectIdAsync(c.Project.Id))
        {
            if (folder.ParentFolderId is null) continue;
            Require(folder.OwnDocumentId is not null, $"The folder {folder.Name} has no own document.");
            Require(File.Exists(Path.Combine(c.Root, await c.FolderPath(folder.Id), $".{folder.Name}.md")), $"The own document of {folder.Name} is not on the disk.");
        }
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        Require(scene.Kind == DocumentKind.Scene && scene.WordGoal == 1000, "The template's scene is missing or has the wrong kind or word goal.");
        Require((await c.DocumentAt("Styles/Default.md")).Kind == DocumentKind.Style, "The template's style sheet is missing.");
    }),
    ("The paths of documents and folders are saved in documents.json and folders.json, not in folder.json", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var documents = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "documents.json")))!["paths"]!;
        var folders = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "folders.json")))!["paths"]!;
        Require(documents[scene.Id]!.GetValue<string>() == "Manuscript/Chapter 01/Scene 01.md", "The document's path is not in documents.json.");
        Require(folders[chapter.Id]!.GetValue<string>() == "Manuscript/Chapter 01", "The folder's path is not in folders.json.");
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["documents"]![scene.Id]!;
        Require(entry["title"] is not null && entry["fileName"] is null && entry["path"] is null, "folder.json still has a file name, or has no details.");
        var place = await c.Places.GetPlaceByDocumentIdAsync(scene.Id);
        await c.Places.UpdateAsync(place with { Path = "Notes/Moved by place.md" }, place.ETag);
        Require(File.Exists(Path.Combine(c.Root, "Notes", "Moved by place.md")), "Updating the place did not move the file.");
        var moved = await c.Documents.GetDocumentByIdAsync(scene.Id);
        Require(moved.FolderId == (await c.FolderAt("Notes")).Id && moved.Kind == DocumentKind.Note && moved.Title == scene.Title,
            "Updating the place did not move the document with its details.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Places.UpdateAsync(place with { Path = "Notes/Again.md" }, place.ETag));
    }),
    ("A folder that another program moves keeps its ID and the IDs and details of its documents", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Documents.UpdateDocumentDetailsAsync(scene.Id, new DocumentDetails("Kept title", "Kept", "", DocumentStatus.Done, 5), scene.ETag);
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
        scene = await c.Documents.GetDocumentByIdAsync(scene.Id);
        Require(await c.Documents.GetDocumentTextByIdAsync(scene.Id) == "Old text", "The front matter was not split off the text.");
        var saved = await c.Documents.UpdateDocumentTextAsync(scene.Id, "New words here", scene.ETag);
        Require(saved.ETag != scene.ETag && saved.WordCount == 3, "The save did not give a new ETag and word count.");
        Require(await File.ReadAllTextAsync(file) == "---\ntags: [draft]\n---\nNew words here", "The front matter was not kept.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Documents.UpdateDocumentTextAsync(scene.Id, "Lost", scene.ETag));
        await Expect(WorkspaceError.ETagMismatch, () => c.Documents.UpdateDocumentTextAsync(scene.Id, "Lost", ""));
    }),
    ("A change to one item does not change the ETag of another item", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        var project = await c.Projects.GetProjectByIdAsync(c.Project.Id);
        await c.Documents.UpdateDocumentTextAsync(scene.Id, "Changed", scene.ETag);
        await c.Folders.UpdateFolderLayoutAsync(notes.Id, new FolderLayout { PinnedView = "board" }, notes.ETag);
        Require((await c.Documents.GetDocumentByIdAsync(style.Id)).ETag == style.ETag, "A change to one document changed another document's ETag.");
        Require((await c.Folders.GetFolderByIdAsync(threads.Id)).ETag == threads.ETag, "A change to one folder changed another folder's ETag.");
        Require((await c.Projects.GetProjectByIdAsync(c.Project.Id)).ETag == project.ETag, "A change to a document or folder changed the project's ETag.");
        await c.Documents.UpdateDocumentTextAsync(style.Id, "Still works", style.ETag);
    }),
    ("Details replace all values, are saved in folder.json, and are read again from the disk", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var details = new DocumentDetails("Opening", "The start.", "Check dates.", DocumentStatus.Revised, 2500);
        scene = await c.Documents.UpdateDocumentDetailsAsync(scene.Id, details, scene.ETag);
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["documents"]![scene.Id]!;
        Require(entry["title"]!.GetValue<string>() == "Opening" && entry["status"]!.GetValue<string>() == "revised" && entry["wordGoal"]!.GetValue<int>() == 2500,
            "The details were not saved in folder.json.");
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var read = await c.Documents.GetDocumentByIdAsync(scene.Id);
        Require(read.Title == "Opening" && read.Synopsis == "The start." && read.Status == DocumentStatus.Revised && read.ETag == scene.ETag,
            "The details were not read again from the disk.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateDocumentDetailsAsync(scene.Id, details with { Title = " " }, scene.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateDocumentDetailsAsync(scene.Id, details with { WordGoal = -1 }, scene.ETag));
    }),
    ("A rename changes the file name and keeps the ID, the details and the links", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var link = await c.Links.CreateLinkAsync(scene.Id, style.Id, "Uses this style");
        var renamed = await c.Documents.RenameDocumentAsync(scene.Id, "The storm", scene.ETag);
        Require(renamed.Id == scene.Id && renamed.Name == "The storm.md" && renamed.Title == scene.Title, "The rename lost the ID or changed the title.");
        Require(File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 01", "The storm.md")) && !File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 01", "Scene 01.md")),
            "The file was not renamed on the disk.");
        Require((await c.Links.GetLinksByDocumentIdAsync(scene.Id)).Single().Id == link.Id, "The link was lost.");
        var second = await c.Documents.CreateDocumentAsync(renamed.FolderId, "Second");
        await Expect(WorkspaceError.Conflict, () => c.Documents.RenameDocumentAsync(second.Id, "The storm", second.ETag));
        var own = await c.Documents.GetDocumentByIdAsync((await c.FolderAt("Manuscript")).OwnDocumentId!);
        await Expect(WorkspaceError.Invalid, () => c.Documents.RenameDocumentAsync(own.Id, "Other", own.ETag));
    }),
    ("A document moves between folders with its file, its details and a new kind", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Documents.UpdateDocumentDetailsAsync(scene.Id, new DocumentDetails("Kept", "", "", DocumentStatus.Draft, 10), scene.ETag);
        var characters = await c.FolderAt("Characters");
        var result = await c.Documents.MoveDocumentToFolderAsync(scene.Id, characters.Id, 0, scene.ETag);
        Require(result.Document.FolderId == characters.Id && result.Document.Kind == DocumentKind.Character && result.Document.Title == "Kept",
            "The moved document has the wrong folder, kind or details.");
        Require(result.NewFolder.ChildIds[0] == scene.Id && !result.OldFolder.ChildIds.Contains(scene.Id), "The folders' orders were not updated.");
        Require(File.Exists(Path.Combine(c.Root, "Characters", "Scene 01.md")), "The file did not move.");
        Require(await c.Places.GetPathByDocumentIdAsync(scene.Id) == "Characters/Scene 01.md", "The document place was not updated.");
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var own = await c.Documents.GetDocumentByIdAsync(chapter.OwnDocumentId!);
        await Expect(WorkspaceError.Invalid, () => c.Documents.MoveDocumentToFolderAsync(own.Id, characters.Id, 0, own.ETag));
        var other = await c.Documents.CreateDocumentAsync(chapter.Id, "Scene 01");
        await Expect(WorkspaceError.Conflict, () => c.Documents.MoveDocumentToFolderAsync(other.Id, characters.Id, 0, other.ETag));
    }),
    ("The order of a folder's children changes only by a move", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var a = await c.Documents.CreateDocumentAsync(chapter.Id, "A");
        var b = await c.Documents.CreateDocumentAsync(chapter.Id, "B");
        chapter = await c.Folders.GetFolderByIdAsync(chapter.Id);
        Require(chapter.ChildIds.TakeLast(2).SequenceEqual([a.Id, b.Id]), "New documents are not at the end.");
        var moved = await c.Documents.MoveDocumentToFolderAsync(b.Id, chapter.Id, 0, b.ETag);
        Require(moved.NewFolder.ChildIds[0] == b.Id && moved.OldFolder.Id == moved.NewFolder.Id, "The reorder did not put the document first.");
        var order = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!["itemOrder"]!.AsArray();
        Require(order[0]!.GetValue<string>() == b.Id, "The order was not saved in folder.json.");
        var inOrder = (await c.Documents.GetDocumentsByFolderIdAsync(chapter.Id)).Select(document => document.Id).ToArray();
        Require(inOrder[0] == b.Id, "The documents of the folder are not in order.");
    }),
    ("A folder moves with everything inside it, and cannot move into itself", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var notes = await c.FolderAt("Notes");
        var result = await c.Folders.MoveFolderToFolderAsync(chapter.Id, notes.Id, 0, chapter.ETag);
        Require(result.Folder.Id == chapter.Id && result.Folder.ParentFolderId == notes.Id && result.NewParentFolder.ChildIds[0] == chapter.Id,
            "The folder did not move into the target folder.");
        Require(Directory.Exists(Path.Combine(c.Root, "Notes", "Chapter 01")), "The folder did not move on the disk.");
        var movedScene = await c.Documents.GetDocumentByIdAsync(scene.Id);
        Require(movedScene.Kind == DocumentKind.Note && await c.Places.GetPathByDocumentIdAsync(scene.Id) == "Notes/Chapter 01/Scene 01.md",
            "The documents inside the folder did not get their new place and kind.");
        var inner = await c.Folders.CreateFolderAsync(chapter.Id, "Inner");
        var moved = await c.Folders.GetFolderByIdAsync(chapter.Id);
        await Expect(WorkspaceError.Invalid, () => c.Folders.MoveFolderToFolderAsync(chapter.Id, inner.Id, 0, moved.ETag));
        var root = await c.Folders.GetFolderByIdAsync(c.Project.RootFolderId);
        await Expect(WorkspaceError.Invalid, () => c.Folders.MoveFolderToFolderAsync(root.Id, notes.Id, 0, root.ETag));
    }),
    ("A folder is deleted only when it is empty, and view settings that name it are cleared", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var empty = await c.Folders.CreateFolderAsync(notes.Id, "Empty");
        var root = await c.Folders.GetFolderByIdAsync(c.Project.RootFolderId);
        await c.Folders.UpdateFolderLayoutAsync(root.Id, Layout("grid", empty.Id), root.ETag);
        var full = await c.Folders.CreateFolderAsync(notes.Id, "Full");
        await c.Documents.CreateDocumentAsync(full.Id, "Inside");
        full = await c.Folders.GetFolderByIdAsync(full.Id);
        await Expect(WorkspaceError.Conflict, () => c.Folders.DeleteFolderAsync(full.Id, full.ETag));
        await c.Folders.DeleteFolderAsync(empty.Id, empty.ETag);
        Require(!Directory.Exists(Path.Combine(c.Root, "Notes", "Empty")), "The empty folder is still on the disk.");
        Require(!(await c.Folders.GetFolderByIdAsync(notes.Id)).ChildIds.Contains(empty.Id), "The deleted folder is still a child.");
        Require(ColumnFolder(await c.Folders.GetFolderByIdAsync(root.Id)) is null, "The view setting that named the deleted folder was not cleared.");
        await Expect(WorkspaceError.NotFound, () => c.Folders.GetFolderByIdAsync(empty.Id));
        var threads = await c.FolderAt("Threads");
        await Expect(WorkspaceError.Forbidden, () => c.Folders.DeleteFolderAsync(threads.Id, threads.ETag));
    }),
    ("Folders keep any view name and settings, and a folder setting must name a folder of the project", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        notes = await c.Folders.UpdateFolderLayoutAsync(notes.Id, new FolderLayout
        {
            PinnedView = "tree", Views = new Dictionary<string, JsonElement> { ["tree"] = Json("""{"depth":3}""") },
        }, notes.ETag);
        notes = await c.Folders.UpdateFolderLayoutAsync(notes.Id, Layout("tree", threads.Id), notes.ETag);
        Require(notes.PinnedView == "tree" && notes.Views["tree"].GetProperty("depth").GetInt32() == 3 && ColumnFolder(notes) == threads.Id,
            "A view the server does not know lost its settings.");
        await Expect(WorkspaceError.Invalid, () => c.Folders.UpdateFolderLayoutAsync(notes.Id, Layout("grid", Guid.NewGuid().ToString()), notes.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Folders.UpdateFolderLayoutAsync(notes.Id,
            new FolderLayout { Views = new Dictionary<string, JsonElement> { ["tree"] = Json("5") } }, notes.ETag));
        notes = await c.Folders.UpdateFolderLayoutAsync(notes.Id, new FolderLayout
        {
            PinnedView = null, Views = new Dictionary<string, JsonElement> { ["tree"] = Json("null") },
        }, notes.ETag);
        Require(notes.PinnedView is null && !notes.Views.ContainsKey("tree") && notes.Views.ContainsKey("grid"), "A JSON null did not remove only that view's settings.");
    }),
    ("Links are saved once, have no direction, and come back when their document comes back from a version", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        var style = await c.DocumentAt("Styles/Default.md");
        var link = await c.Links.CreateLinkAsync(scene.Id, style.Id, " A note ");
        Require(link.Note == "A note", "The note was not trimmed.");
        await Expect(WorkspaceError.Conflict, () => c.Links.CreateLinkAsync(style.Id, scene.Id));
        await Expect(WorkspaceError.Invalid, () => c.Links.CreateLinkAsync(scene.Id, scene.Id));
        link = await c.Links.UpdateLinkNoteAsync(link.Id, "Changed", link.ETag);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "links.json")))!["links"]!.AsArray();
        Require(file.Count == 1 && file[0]!["note"]!.GetValue<string>() == "Changed", "The link is not saved once in links.json.");
        var version = await c.History.SaveVersionAsync(c.Project.Id, "With the style sheet");
        File.Delete(Path.Combine(c.Root, "Styles", "Default.md"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        Require((await c.Links.GetLinksByProjectIdAsync(c.Project.Id)).Count == 0, "A link to a deleted document is still listed.");
        await c.History.RestoreProjectVersionAsync(c.Project.Id, version.Id);
        var restored = (await c.Links.GetLinksByProjectIdAsync(c.Project.Id)).SingleOrDefault();
        Require(restored?.Id == link.Id && restored.Note == "Changed", "The link did not come back with its document.");
        await c.Links.DeleteLinkAsync(restored!.Id, restored.ETag);
        Require((await c.Links.GetLinksByDocumentIdAsync(scene.Id)).Count == 0, "The link was not deleted.");
    }),
    ("Files and folders that other programs add, change, rename or copy are read correctly", async c =>
    {
        var characters = await c.FolderAt("Characters");
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Characters", "Outside.md"), "Made outside.");
        Directory.CreateDirectory(Path.Combine(c.Root, "Notes", "Outside folder"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        var outside = await c.DocumentAt("Characters/Outside.md");
        Require(outside.Kind == DocumentKind.Character && outside.Title == "Outside", "A file added outside did not become a document.");
        Require((await c.Folders.GetFolderByIdAsync(characters.Id)).ChildIds[^1] == outside.Id, "The added document is not at the end of its folder.");
        var added = await c.FolderAt("Notes/Outside folder");
        Require(added.OwnDocumentId is not null && File.Exists(Path.Combine(c.Root, "Notes", "Outside folder", ".odysseum", "folder.json")),
            "A folder added outside did not get its settings file and own document.");

        await File.AppendAllTextAsync(Path.Combine(c.Root, "Characters", "Outside.md"), " More.");
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        Require((await c.Documents.GetDocumentByIdAsync(outside.Id)).ETag != outside.ETag, "A change outside did not change the ETag.");

        File.Move(Path.Combine(c.Root, "Characters", "Outside.md"), Path.Combine(c.Root, "Characters", "Renamed.md"));
        await c.Storage.ReloadProjectAsync(c.Project.Id);
        await Expect(WorkspaceError.NotFound, () => c.Documents.GetDocumentByIdAsync(outside.Id));
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
        Require((await second.Projects.GetAllProjectsAsync()).All(project => project.Id != c.Project.Id), "A second server opened a project that is open.");
    }),
    ("Versions are saved, listed, read and restored for the project and for one document", async c =>
    {
        var scene = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        scene = await c.Documents.UpdateDocumentTextAsync(scene.Id, "First version", scene.ETag);
        var version = await c.History.SaveVersionAsync(c.Project.Id, "Checkpoint");
        scene = await c.Documents.UpdateDocumentTextAsync(scene.Id, "Second version", scene.ETag);
        await c.History.SaveVersionAsync(c.Project.Id, "Later");
        Require((await c.History.GetVersionsByProjectIdAsync(c.Project.Id)).Any(item => item.Name == "Checkpoint"), "The named version is not listed.");
        Require((await c.History.GetVersionsByDocumentIdAsync(scene.Id)).Count >= 2, "The document's versions are not listed.");
        Require(await c.History.GetDocumentTextFromVersionAsync(scene.Id, version.Id) == "First version", "The text from the version is wrong.");
        await c.History.RestoreDocumentVersionAsync(scene.Id, version.Id);
        Require(await c.Documents.GetDocumentTextByIdAsync(scene.Id) == "First version", "The document was not restored.");
        await c.Documents.UpdateDocumentTextAsync(scene.Id, "Third", (await c.Documents.GetDocumentByIdAsync(scene.Id)).ETag);
        await c.Folders.CreateFolderAsync((await c.FolderAt("Notes")).Id, "After the version");
        await c.History.RestoreProjectVersionAsync(c.Project.Id, version.Id);
        Require(await c.Documents.GetDocumentTextByIdAsync(scene.Id) == "First version", "The project was not restored.");
        Require((await c.Folders.GetFoldersByProjectIdAsync(c.Project.Id)).All(folder => folder.Name != "After the version"),
            "A folder made after the version is still there after the restore.");
        await Expect(WorkspaceError.Invalid, () => c.History.SaveVersionAsync(c.Project.Id, " "));
    }),
    ("Search finds text, titles and notes, and export writes the scenes in manuscript order", async c =>
    {
        var chapter = await c.FolderAt("Manuscript/Chapter 01");
        var first = await c.DocumentAt("Manuscript/Chapter 01/Scene 01.md");
        await c.Documents.UpdateDocumentTextAsync(first.Id, "The lighthouse stood alone.", first.ETag);
        var second = await c.Documents.CreateDocumentAsync(chapter.Id, "Second scene", "Waves.");
        second = await c.Documents.UpdateDocumentDetailsAsync(second.Id, new DocumentDetails("Second scene", "", "Mentions a lighthouse", DocumentStatus.Draft, 0), second.ETag);
        var results = await c.Documents.SearchDocumentsAsync(c.Project.Id, "LIGHTHOUSE");
        Require(results.Select(result => result.Document.Id).SequenceEqual([first.Id, second.Id]), "Search did not find both documents in order.");
        Require(results[0].Excerpt.Contains("lighthouse"), "The excerpt does not show the match.");
        var export = await new ManuscriptExportService(c.Projects, c.Documents).ExportManuscriptAsync(c.Project.Id);
        Require(export.StartsWith("# Test") && export.IndexOf("The lighthouse", StringComparison.Ordinal) < export.IndexOf("Waves.", StringComparison.Ordinal)
            && !export.Contains(".normal"), "The export is not the scenes in manuscript order.");
    }),
    ("A project saved as a template makes a project with the same folders, documents and layouts", async c =>
    {
        var notes = await c.FolderAt("Notes");
        var threads = await c.FolderAt("Threads");
        await c.Folders.UpdateFolderLayoutAsync(notes.Id, Layout("grid", threads.Id), notes.ETag);
        await c.Documents.CreateDocumentAsync(notes.Id, "Idea");
        var template = await c.Workspace.Templates.CaptureTemplateAsync(c.Project.Id, "Mine");
        Require(template.Folders.Single(folder => folder.Path == "Notes").Views!["grid"].GetProperty("columnFolder").GetString() == "Threads",
            "The template does not name the column folder by path.");
        c.Workspace.TemplateFiles.Save(template);
        var made = await c.Projects.CreateProjectAsync("From mine", templateName: "Mine");
        var madeNotes = (await c.Folders.GetFoldersByProjectIdAsync(made.Id)).Single(folder => folder.Name == "Notes" && folder.ParentFolderId == made.RootFolderId);
        var madeThreads = (await c.Folders.GetFoldersByProjectIdAsync(made.Id)).Single(folder => folder.Name == "Threads" && folder.ParentFolderId == made.RootFolderId);
        Require(madeNotes.PinnedView == "grid" && ColumnFolder(madeNotes) == madeThreads.Id, "The layout was not made in the new project.");
        Require((await c.Documents.GetDocumentsByFolderIdAsync(madeNotes.Id)).Any(document => document.Title == "Idea"), "The document was not made in the new project.");
        await Expect(WorkspaceError.NotFound, () => c.Projects.CreateProjectAsync("Missing", templateName: "No such template"));
    }),
    ("Project settings replace all values and check them", async c =>
    {
        var project = await c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("Renamed", 90000, 1500), c.Project.ETag);
        Require(project.Title == "Renamed" && project.WordGoal == 90000 && project.DefaultSceneWordGoal == 1500, "The settings were not saved.");
        await Expect(WorkspaceError.ETagMismatch, () => c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("Old", 1, 1), c.Project.ETag));
        await Expect(WorkspaceError.Invalid, () => c.Projects.UpdateProjectSettingsAsync(c.Project.Id, new ProjectSettings("", 1, 1), project.ETag));
        var created = await c.Documents.CreateDocumentAsync((await c.FolderAt("Manuscript")).Id, "New");
        Require(created.WordGoal == 1500, "A new document does not get the project's default word goal.");
    }),
    ("Names that are not allowed are refused", async c =>
    {
        var notes = await c.FolderAt("Notes");
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateFolderAsync(notes.Id, "a/b"));
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateFolderAsync(notes.Id, ".hidden"));
        await Expect(WorkspaceError.Invalid, () => c.Documents.CreateDocumentAsync(notes.Id, "  "));
        await c.Folders.CreateFolderAsync(notes.Id, "Twice");
        await Expect(WorkspaceError.Conflict, () => c.Folders.CreateFolderAsync(notes.Id, "twice"));
        var first = await c.Documents.CreateDocumentAsync(notes.Id, "Same");
        var second = await c.Documents.CreateDocumentAsync(notes.Id, "Same");
        Require(first.Name == "Same.md" && second.Name == "Same-2.md", "A second document with the same title did not get a free file name.");
    }),
};

var libraryChecks = new List<(string Name, Func<string, Task> Run)>
{
    ("The watcher reports changes from other programs, but not the server's own writes", async root =>
    {
        await using var w = await Workspace.StartAsync(root, watch: true);
        var project = await w.Projects.CreateProjectAsync("Watched");
        var scene = (await w.Documents.GetDocumentsByProjectIdAsync(project.Id)).Single(document => document.Name == "Scene 01.md");
        var updates = 0;
        w.Documents.DocumentUpdated += (_, _) => Interlocked.Increment(ref updates);
        scene = await w.Documents.UpdateDocumentTextAsync(scene.Id, "Typed in the app", scene.ETag);
        await Task.Delay(1500);
        Require(Volatile.Read(ref updates) == 1, "The server's own write was reported more than once.");
        await File.AppendAllTextAsync(Path.Combine(root, project.Name, "Manuscript", "Chapter 01", "Scene 01.md"), "\nTyped outside.");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await w.Documents.GetDocumentByIdAsync(scene.Id)).ETag == scene.ETag && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require((await w.Documents.GetDocumentByIdAsync(scene.Id)).ETag != scene.ETag, "An edit made outside was not read.");
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
static void CopyFolder(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    foreach (var folder in Directory.EnumerateDirectories(source)) CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
}
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
    private Workspace(string root, ISettingsProvider? settings, bool watch)
    {
        OwnWrites = new OwnWrites();
        Watcher = watch ? new FileProjectWatcher(root, OwnWrites, 300) : new NullProjectWatcher();
        History = new GitProjectHistory(root);
        Storage = new DiskStorageContext(root, Watcher, OwnWrites);
        ProjectRepository = new ProjectRepository(Storage);
        FolderPlaces = new FolderPlaceRepository(Storage);
        DocumentPlaces = new DocumentPlaceRepository(Storage);
        FolderRepository = new FolderRepository(Storage, FolderPlaces);
        DocumentRepository = new DocumentRepository(Storage, DocumentPlaces);
        LinkRepository = new LinkRepository(Storage);
        TemplateFiles = new TemplateRepository(Path.Combine(root, "..", Path.GetFileName(root) + "-templates"));
        Folders = new FolderService(FolderRepository, FolderPlaces, DocumentRepository, DocumentPlaces, Storage, settings);
        Documents = new DocumentService(DocumentRepository, DocumentPlaces, FolderRepository, FolderPlaces, ProjectRepository, Storage);
        Templates = new ProjectTemplateService(Folders, Documents, FolderRepository, DocumentRepository, FolderPlaces, DocumentPlaces, ProjectRepository);
        Projects = new ProjectService(ProjectRepository, Storage, Templates, TemplateFiles);
        Links = new LinkService(LinkRepository, DocumentRepository);
        HistoryService = new HistoryService(ProjectRepository, FolderRepository, DocumentRepository, LinkRepository, DocumentPlaces, History, Storage, 60);
    }

    /// <summary>Makes the workspace and reads its projects.</summary>
    public static async Task<Workspace> StartAsync(string root, ISettingsProvider? settings = null, bool watch = false)
    {
        var workspace = new Workspace(root, settings, watch);
        await workspace.Storage.LoadAllProjectsAsync();
        return workspace;
    }

    public OwnWrites OwnWrites { get; }
    public IProjectWatcher Watcher { get; }
    public GitProjectHistory History { get; }
    public DiskStorageContext Storage { get; }
    public ProjectRepository ProjectRepository { get; }
    public FolderPlaceRepository FolderPlaces { get; }
    public DocumentPlaceRepository DocumentPlaces { get; }
    public FolderRepository FolderRepository { get; }
    public DocumentRepository DocumentRepository { get; }
    public LinkRepository LinkRepository { get; }
    public TemplateRepository TemplateFiles { get; }
    public ProjectTemplateService Templates { get; }
    public IProjectService Projects { get; }
    public IFolderService Folders { get; }
    public IDocumentService Documents { get; }
    public Odysseum.Abstractions.Links.ILinkService Links { get; }
    public HistoryService HistoryService { get; }

    public async ValueTask DisposeAsync()
    {
        await HistoryService.DisposeAsync();
        await Storage.DisposeAsync();
        await Watcher.DisposeAsync();
        History.Dispose();
    }
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
    public IFolderService Folders => workspace.Folders;
    public IDocumentService Documents => workspace.Documents;
    public Odysseum.Abstractions.Links.ILinkService Links => workspace.Links;
    public HistoryService History => workspace.HistoryService;
    public DocumentPlaceRepository Places => workspace.DocumentPlaces;

    public Task<string> FolderPath(string folderId) => workspace.FolderPlaces.GetPathByFolderIdAsync(folderId);

    public async Task<IFolder> FolderAt(string path)
    {
        var place = workspace.FolderPlaces.Query().SingleOrDefault(place => place.ProjectId == project.Id && place.Path == path)
            ?? throw new Exception($"There is no folder at '{path}'.");
        return await Folders.GetFolderByIdAsync(place.FolderId);
    }

    public async Task<IDocument> DocumentAt(string path)
    {
        var place = workspace.DocumentPlaces.Query().SingleOrDefault(place => place.ProjectId == project.Id && place.Path == path)
            ?? throw new Exception($"There is no document at '{path}'.");
        return await Documents.GetDocumentByIdAsync(place.DocumentId);
    }
}

/// <summary>Server settings with their default values: default project folders cannot be deleted.</summary>
sealed class DefaultServerSettings : ISettingsProvider
{
    public IServerSettings GetSettings(bool copy = false) => new ServerSettings();
    public void SaveSettings(IServerSettings settings) { }
    public void SaveSettings() { }
    public void DebugSettingsToLog() { }
}
