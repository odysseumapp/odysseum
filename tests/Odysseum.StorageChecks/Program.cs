using System.Text;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Views;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Disk;
using Odysseum.Server.Repositories.Git;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<Check, Task> Run)>
{
    ("Folders retain layouts and identity through scans and external moves", async c =>
    {
        var project = await c.Project();
        var topics = await c.Folders.CreateAsync(c.Branch, project.Root.Id, "Topics");
        var race = await c.Folders.CreateAsync(c.Branch, topics.Id, "Race");
        var doc = await c.Documents.CreateAsync(c.Branch, race.Id, "Revelation", "Text");
        await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Threads")).Id, "Race", "Thread notes");
        project = await c.Project();
        var threads = FolderAt(project, "Threads");
        race = await c.Folders.SetLayoutAsync(c.Branch, FolderAt(project, "Topics/Race").Id, new FolderLayout { PinnedView = FolderView.Grid, GridFolderId = threads.Id }, project.Revision);
        project = await c.Project();
        await c.Folders.MoveAsync(c.Branch, doc.Id, race.Id, 0, project.Revision);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Topics/Race/.odysseum/folder.json")))!;
        Require(manifest["pinnedView"]!.GetValue<string>() == "grid", "Pin was not stored in the owning folder.");
        Require(manifest["itemOrder"]![0]!.GetValue<string>() == doc.Id, "Child order was not stored in the owning folder.");
        Directory.Move(Path.Combine(c.Root, "Topics/Race"), Path.Combine(c.Root, "Topics/Class"));
        var moved = FolderAt(await c.Project(), "Topics/Class");
        Require(moved.Id == race.Id && moved.PinnedView == FolderView.Grid && moved.GridFolderId == threads.Id, "Folder layout or identity was lost on move.");
    }),
    ("Folder removal refuses content and archives empty folder metadata", async c =>
    {
        var project = await c.Project();
        var empty = await c.Folders.CreateAsync(c.Branch, project.Root.Id, "Empty");
        var id = empty.Id;
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Empty/.keep"), "Keep this hidden file");
        project = await c.Project();
        await Expect(WorkspaceError.Conflict, () => c.Folders.RemoveAsync(c.Branch, empty.Id, project.Revision));
        File.Delete(Path.Combine(c.Root, "Empty/.keep"));
        project = await c.Project();
        await c.Folders.RemoveAsync(c.Branch, FolderAt(project, "Empty").Id, project.Revision);
        project = await c.Project();
        Require(project.FolderAt("Empty") is null && !Directory.Exists(Path.Combine(c.Root, "Empty")), "Empty folder remains visible.");
        var archived = Directory.GetFiles(Path.Combine(c.Root, ".odysseum/removed-folders"), "folder.json", SearchOption.AllDirectories);
        Require(archived.Length == 1 && (await File.ReadAllTextAsync(archived[0])).Contains(id), "Removed metadata was lost.");
        await Expect(WorkspaceError.Invalid, () => c.Folders.RemoveAsync(c.Branch, project.Root.Id, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateAsync(c.Branch, project.Root.Id, "../Outside"));
    }),
    ("Folder layouts validate their grid folder and reject stale writes", async c =>
    {
        var first = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "One")).Id, "First", "");
        var other = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Two")).Id, "Other", "");
        var project = await c.Project();
        var one = FolderAt(project, "One");
        var two = FolderAt(project, "Two");
        await Expect(WorkspaceError.NotFound, () => c.Folders.MoveAsync(c.Branch, other.Id, Guid.NewGuid().ToString(), 0, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(c.Branch, one.Id, new FolderLayout { PinnedView = FolderView.Board, GridFolderId = Guid.NewGuid().ToString() }, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(c.Branch, one.Id, new FolderLayout { PinnedView = FolderView.Board, GridFolderId = first.Id }, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(c.Branch, one.Id, new FolderLayout { PinnedView = (FolderView)99 }, project.Revision));
        var root = await c.Folders.SetLayoutAsync(c.Branch, project.Root.Id, new FolderLayout { PinnedView = FolderView.Outline, GridFolderId = two.Id }, project.Revision);
        project = await c.Project();
        await c.Folders.MoveAsync(c.Branch, two.Id, root.Id, 0, project.Revision);
        project = await c.Project();
        var stale = project.Revision;
        await c.Folders.CreateAsync(c.Branch, project.Root.Id, "Three");
        await Expect(WorkspaceError.Conflict, () => c.Folders.SetLayoutAsync(c.Branch, project.Root.Id, new FolderLayout { PinnedView = FolderView.Board }, stale));
        project = await c.Project();
        Require(project.Root.GridFolderId == two.Id, "Grid column folder was lost.");
        Require(project.Root.Folders.Select(f => f.Id).SequenceEqual([two.Id, one.Id, FolderAt(project, "Three").Id]), "Root folder order was lost.");
        await Expect(WorkspaceError.Conflict, () => c.Folders.RemoveAsync(c.Branch, FolderAt(project, "One").Id, project.Revision));
    }),
    ("Reads an existing folder without rewriting its Markdown", async c =>
    {
        var path = Path.Combine(c.Root, "existing.md");
        var original = Encoding.UTF8.GetBytes("# Existing\n\nText with **meaning**.\n");
        await File.WriteAllBytesAsync(path, original);
        var project = await c.Project();
        Require(project.Documents.Count(Visible) == 1, "Document was not discovered.");
        var after = await File.ReadAllBytesAsync(path);
        Require(original.SequenceEqual(after), "Scan rewrote the file.");
    }),
    ("Preserves UTF-8 BOM, CRLF, frontmatter, and unsupported Markdown", async c =>
    {
        var id = Guid.NewGuid().ToString();
        var prefix = $"\uFEFF---\r\nwriter_id: {id}\r\ncustom: [keep, exactly]\r\n---\r\n\r\n";
        const string body = "Paragraph.\r\n\r\n[^note]: unknown extension\r\n<div data-test=\"yes\">HTML stays text</div>\r\n";
        var path = Path.Combine(c.Root, "format.md");
        await File.WriteAllTextAsync(path, prefix + body, new UTF8Encoding(false));
        var doc = await Open(c, id);
        Require(doc.Body == body, "Frontmatter was not split exactly.");
        await c.Documents.SaveBodyAsync(c.Branch, doc.Id, body + "Next paragraph.\r\n", doc.Revision);
        Require(await File.ReadAllTextAsync(path) == prefix.TrimStart('\uFEFF') + body + "Next paragraph.\r\n", "File formatting was changed.");
        Require((await File.ReadAllBytesAsync(path)).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "BOM was lost.");
    }),
    ("Rejects a stale browser save after an external edit with unchanged timestamps", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Conflict", "Original text");
        var path = Path.Combine(c.Root, PathOf(doc));
        var timestamp = File.GetLastWriteTimeUtc(path);
        var raw = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, raw.Replace("Original text", "External text"));
        File.SetLastWriteTimeUtc(path, timestamp);
        await Expect(WorkspaceError.Conflict, () => c.Documents.SaveBodyAsync(c.Branch, doc.Id, "Browser text", doc.Revision));
        Require((await File.ReadAllTextAsync(path)).Contains("External text"), "External edits were overwritten.");
    }),
    ("Serializes simultaneous browser saves so exactly one succeeds", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Two tabs", "Start");
        async Task<bool> Save(string text)
        {
            try { await c.Documents.SaveBodyAsync(c.Branch, doc.Id, text, doc.Revision); return true; }
            catch (WorkspaceException ex) when (ex.Error == WorkspaceError.Conflict) { return false; }
        }
        var results = await Task.WhenAll(Save("First tab"), Save("Second tab"));
        Require(results.Count(x => x) == 1, "Expected one success and one conflict.");
    }),
    ("Serializes competing settings and document metadata updates", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Original", "Text");
        var before = await c.Project();
        async Task<bool> Attempt(Func<Task> change)
        {
            try { await change(); return true; }
            catch (WorkspaceException ex) when (ex.Error == WorkspaceError.Conflict) { return false; }
        }
        var results = await Task.WhenAll(
            Attempt(() => c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = "Updated project", WordGoal = 12345 }, before.Revision)),
            Attempt(() => c.Documents.UpdateAsync(c.Branch, doc.Id, Details("Updated document", "", "", DocumentStatus.Revised, 500), before.Revision)));
        Require(results.Count(success => success) == 1, "Expected exactly one metadata revision to succeed.");
        var after = await c.Project();
        Require(after.Title == (results[0] ? "Updated project" : before.Title), "Settings did not match the successful update.");
        Require(after.Documents.Where(Visible).Single().Title == (results[1] ? "Updated document" : "Original"), "Rejected document metadata leaked into state.");
    }),
    ("Retains metadata through an external move and content edit", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "A scene", "Before");
        var project = await c.Project();
        await c.Documents.UpdateAsync(c.Branch, doc.Id, Details("A better title", "A synopsis", "Private notes", DocumentStatus.Revised, 900), project.Revision);
        var oldPath = Path.Combine(c.Root, PathOf(doc));
        var target = Path.Combine(c.Root, "Other", "renamed.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(oldPath, target);
        await File.AppendAllTextAsync(target, "\nExternal addition.");
        var moved = await Open(c, doc.Id);
        Require(PathOf(moved) == "Other/renamed.md" && moved.Synopsis == "A synopsis" && moved.Title == "A better title", "Identity or metadata was lost.");
        Require(moved.Body!.Contains("External addition"), "External edit was missed.");
    }),
    ("Does not recreate externally deleted documents", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Deleted", "Before");
        var path = Path.Combine(c.Root, PathOf(doc));
        File.Delete(path);
        await Expect(WorkspaceError.NotFound, () => c.Documents.SaveBodyAsync(c.Branch, doc.Id, "Unsaved", doc.Revision));
        Require(!File.Exists(path), "A deleted file was recreated.");
    }),
    ("Checks metadata revisions and preserves unknown JSON fields", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Metadata", "Text");
        var oldProject = await c.Project();
        var path = Path.Combine(c.Root, ".odysseum", "project.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["customTool"] = "keep me";
        json["documents"]![doc.Id]!["customField"] = 42;
        json["settings"]!["title"] = "External project name";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Expect(WorkspaceError.Conflict, () => c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = "Stale browser title", WordGoal = 100 }, oldProject.Revision));
        var current = await c.Project();
        await c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = "Updated title", WordGoal = 200 }, current.Revision);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Require(saved["customTool"]!.GetValue<string>() == "keep me" && saved["documents"]![doc.Id]!["customField"]!.GetValue<int>() == 42, "Unknown fields were discarded.");
    }),
    ("Refuses malformed metadata without overwriting it", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Protected", "Keep this");
        var path = Path.Combine(c.Root, ".odysseum", "project.json");
        await File.WriteAllTextAsync(path, "{ broken external edit");
        await Expect(WorkspaceError.Corrupt, () => c.Documents.SaveBodyAsync(c.Branch, doc.Id, "New text", doc.Revision));
        Require(await File.ReadAllTextAsync(path) == "{ broken external edit", "Malformed metadata was replaced.");
    }),
    ("Blocks traversal and hidden internal paths", async c =>
    {
        var project = await c.Project();
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, "../outside"));
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, ".odysseum"));
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, "C:/outside"));
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateAsync(c.Branch, project.Root.Id, ".hidden"));
        ExpectSync(WorkspaceError.Invalid, () => FolderPaths.Split("../escape.md"));
        var doc = await c.Documents.CreateAsync(c.Branch, project.Root.Id, "Safe", "Text");
        var moved = await c.Documents.MoveAsync(c.Branch, doc.Id, project.Root.Id, "../escape", doc.Revision);
        Require(PathOf(moved) == "escape.md", "A traversal attempt in a file name was not neutralised.");
    }),
    ("Rejects copied document IDs without confusing their content", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Original", "Keep");
        File.Copy(Path.Combine(c.Root, PathOf(doc)), Path.Combine(c.Root, "copied.md"));
        await Expect(WorkspaceError.Conflict, () => c.Project());
        Require((await File.ReadAllTextAsync(Path.Combine(c.Root, "copied.md"))).Contains("Keep"), "Copy was modified.");
    }),
    ("Exports manuscript order and excludes story notes", async c =>
    {
        var manuscript = await Ensure(c, "Manuscript");
        var first = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "First", "First body");
        var second = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "Second", "Second body");
        await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Notes")).Id, "Secret notes", "Research only");
        var project = await c.Project();
        await c.Folders.MoveAsync(c.Branch, second.Id, manuscript.Id, 0, project.Revision);
        var export = await c.Views.ExportAsync(await c.Project());
        Require(export.IndexOf("Second body", StringComparison.Ordinal) < export.IndexOf("First body", StringComparison.Ordinal), "Export order is wrong.");
        Require(!export.Contains("Research only") && !export.Contains("writer_id"), "Export included internal metadata or notes.");
        Require(first.Id != second.Id, "Documents must have their own ids.");
    }),
    ("Searches prose, synopsis, and author notes", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Find me", "The cartographer left at dawn.");
        var project = await c.Project();
        await c.Documents.UpdateAsync(c.Branch, doc.Id, Details("Find me", "A lighthouse", "Remember Bellwether", DocumentStatus.Draft, 500), project.Revision);
        foreach (var query in new[] { "CARTOGRAPHER", "lighthouse", "Bellwether" })
            Require((await c.Views.SearchAsync(await c.Project(), query)).Single().Document.Id == doc.Id, "Search missed " + query);
    }),
    ("Migrates a flat legacy manifest and applies the default scene goal", async c =>
    {
        var path = Path.Combine(c.Root, ".odysseum", "project.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"version\": 1, \"id\": \"" + Guid.NewGuid() + "\", \"title\": \"Legacy title\", \"wordGoal\": 12345, \"documents\": {}}");
        var project = await c.Project();
        Require(project.Title == "Legacy title" && project.WordGoal == 12345, "Legacy top-level settings were not migrated.");
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Require(written["settings"]!["title"]!.GetValue<string>() == "Legacy title" && written["title"] is null, "Manifest was not rewritten in the grouped form.");
        await Expect(WorkspaceError.Invalid, () => c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = " ", WordGoal = 1 }, project.Revision));
        var updated = await c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = "Legacy title", WordGoal = 12345, DefaultSceneWordGoal = 700 }, project.Revision);
        var doc = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Fresh scene", "Text");
        Require(doc.WordGoal == 700 && updated.DefaultSceneWordGoal == 700, "New scenes should take the project's default goal.");
    }),
    ("Classifies documents by folder and attaches characters to scenes", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "Text");
        var character = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "A cartographer.");
        var note = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Notes")).Id, "Island", "Research");
        Require(scene.Kind == DocumentKind.Scene && character.Kind == DocumentKind.Character && note.Kind == DocumentKind.Note, "Kinds were not derived from folders.");
        var project = await c.Project();
        var updated = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [character.Id]), project.Revision);
        Require(updated.Document(scene.Id)!.Links.SequenceEqual([character.Id]), "Attached characters were not stored.");
        Require(updated.Document(character.Id)!.Links.SequenceEqual([scene.Id]), "Links are undirected: the character should list the scene.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [scene.Id]), updated.Revision));
        Require(!(await c.Views.ExportAsync(await c.Project())).Contains("A cartographer."), "Characters must not appear in the manuscript export.");
    }),
    ("An invalid metadata request leaves existing details unchanged", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await c.Project()).Root.Id, "Keep title", "Text");
        var project = await c.Project();
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, doc.Id, Details("Wrong title", "", "", (DocumentStatus)99, 5), project.Revision));
        Require((await c.Project()).Document(doc.Id)!.Title == "Keep title", "Rejected request partially changed metadata.");
    }),
    ("Rejected character attachments never persist other metadata changes", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Keep title", "Text");
        var character = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "Biography");
        var project = await c.Project();
        project = await Update(c, scene, Details("Keep title", "Keep synopsis", "Keep notes", DocumentStatus.Draft, 1000, [character.Id]), project.Revision);
        var manifestPath = Path.Combine(c.Root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        foreach (var attachments in new[] { new[] { scene.Id }, Enumerable.Repeat(character.Id, 201).ToArray() })
        {
            await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, scene.Id,
                Details("Rejected title", "Rejected synopsis", "Rejected notes", DocumentStatus.Revised, 5, attachments), project.Revision));
            var current = await c.Project();
            var details = current.Document(scene.Id)!;
            Require(details.Title == "Keep title" && details.Synopsis == "Keep synopsis" && details.Notes == "Keep notes"
                && details.Status == DocumentStatus.Draft && details.WordGoal == 1000
                && details.Links.SequenceEqual([character.Id]), "Rejected request changed the in-memory metadata.");
            var persisted = await File.ReadAllBytesAsync(manifestPath);
            Require(current.Revision == project.Revision && original.SequenceEqual(persisted),
                "A later scan persisted a rejected request.");
        }
    }),
    ("Failed scans do not publish partially discovered metadata", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Original", "Keep this");
        var before = await c.Project();
        var candidate = Path.Combine(c.Root, "A new file.md");
        var duplicate = Path.Combine(c.Root, "Z duplicate.md");
        await File.WriteAllTextAsync(candidate, "Discovered before the duplicate ID.");
        File.Copy(Path.Combine(c.Root, PathOf(scene)), duplicate);
        await Expect(WorkspaceError.Conflict, () => c.Project());
        File.Delete(candidate);
        File.Delete(duplicate);
        var after = await c.Project();
        Require(after.Revision == before.Revision && after.Documents.Count(Visible) == 1,
            "An aborted scan left partial changes in the manifest.");
    }),
    ("Failed manifest replacements leave metadata, settings, and order unchanged", async c =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var manuscript = await Ensure(c, "Manuscript");
        var first = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "First", "Text");
        var second = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "Second", "Text");
        var before = await c.Project();
        var manifestPath = Path.Combine(c.Root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        Func<Task>[] changes =
        [
            () => c.Documents.UpdateAsync(c.Branch, first.Id, Details("Rejected", "Synopsis", "Notes", DocumentStatus.Revised, 5), before.Revision),
            () => c.Projects.SaveSettingsAsync(c.Branch, new ProjectSettings { Title = "Rejected settings", WordGoal = 5 }, before.Revision),
            () => c.Folders.MoveAsync(c.Branch, second.Id, manuscript.Id, 0, before.Revision),
        ];
        foreach (var change in changes)
        {
            var failed = false;
            using (var locked = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var folderLocked = new FileStream(Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { await change(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            }
            Require(failed, "Expected the locked manifest to reject replacement.");
            var after = await c.Project();
            var persisted = await File.ReadAllBytesAsync(manifestPath);
            Require(after.Revision == before.Revision && original.SequenceEqual(persisted),
                "A later scan persisted changes from a failed replacement.");
            Require(!Directory.EnumerateFiles(Path.GetDirectoryName(manifestPath)!, ".odysseum-*.tmp").Any(),
                "A failed replacement left a temporary file behind.");
        }
    }),
    ("Null document metadata is rejected without changing the manifest", async c =>
    {
        var doc = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Keep", "Text");
        var manifestPath = Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        manifest["documents"]![doc.Id] = null;
        var invalid = manifest.ToJsonString();
        await File.WriteAllTextAsync(manifestPath, invalid);
        await Expect(WorkspaceError.Corrupt, () => c.Project());
        Require(await File.ReadAllTextAsync(manifestPath) == invalid, "Invalid metadata was rewritten.");
    }),
    ("A link carries one note shared by both ends, and the note goes when the link does", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "Scene prose");
        var thread = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Threads")).Id, "Race", "Thread notes");
        var other = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "Biography");
        var project = await c.Project();
        project = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [thread.Id],
            new() { [thread.Id] = "  Mara first doubts the map  ", [other.Id] = "Not linked" }), project.Revision);
        var notes = project.Document(scene.Id)!.LinkNotes;
        Require(notes.Count == 1 && notes[thread.Id] == "Mara first doubts the map", "The note was not stored trimmed, or a note without a link was kept.");
        Require(project.Document(thread.Id)!.LinkNotes[scene.Id] == "Mara first doubts the map", "The other end should read the same note.");
        var threadManifest = Path.Combine(c.Root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![thread.Id]!["linkNotes"]![scene.Id]!.GetValue<string>() == "Mara first doubts the map", "The note was not persisted on the other side.");
        project = await Update(c, thread, Details("Race", "", "", DocumentStatus.Draft, 1000, null, new() { [scene.Id] = "Rewritten" }), project.Revision);
        project = await Update(c, scene, Details("Arrival", "Synopsis", "", DocumentStatus.Draft, 1000), project.Revision);
        Require(project.Document(scene.Id)!.LinkNotes[thread.Id] == "Rewritten", "A note edited from the other end did not change here, or an unrelated save lost it.");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!;
        manifest["documents"]![thread.Id]!["linkNotes"] = new JsonObject();
        await File.WriteAllTextAsync(threadManifest, manifest.ToJsonString());
        project = await c.Project();
        Require(project.Document(thread.Id)!.LinkNotes[scene.Id] == "Rewritten", "A one-sided note should read from the other end.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new() { [thread.Id] = new string('x', 2001) }), project.Revision));
        project = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new()), project.Revision);
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "An empty note set should clear the note on both sides.");
        project = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new() { [thread.Id] = "Back" }), project.Revision);
        project = await Update(c, thread, Details("Race", "", "", DocumentStatus.Draft, 1000, []), project.Revision);
        project = await Update(c, thread, Details("Race", "", "", DocumentStatus.Draft, 1000, [scene.Id]), project.Revision);
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "Unlinking should take the note with it on both sides.");
    }),
    ("Links are undirected, kept on both sides, and legacy character, location and thread lists migrate", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "Scene prose");
        var first = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Threads")).Id, "Race", "Thread notes only");
        var second = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Threads/Story Beats")).Id, "Meet Cute", "A nested thread");
        var mara = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "Biography");
        Require(first.Kind == DocumentKind.Thread && second.Kind == DocumentKind.Thread, "Thread file was classified as a scene.");
        var project = await c.Project();
        var positionBefore = Position(project, scene.Id);
        project = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [first.Id, second.Id]), project.Revision);
        var current = project.Document(scene.Id)!;
        Require(current.Links.SequenceEqual(new[] { first.Id, second.Id }) && Position(project, scene.Id) == positionBefore, "Linking changed manuscript order or lost a link.");
        Require(project.Document(first.Id)!.Links.SequenceEqual([scene.Id]), "The thread should list the scene back.");
        var threadManifest = Path.Combine(c.Root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![first.Id]!["links"]![0]!.GetValue<string>() == scene.Id, "The reverse side was not persisted.");
        project = await Update(c, second, Details("Meet Cute", "", "", DocumentStatus.Draft, 1000, [scene.Id, first.Id]), project.Revision);
        Require(project.Document(first.Id)!.Links.OrderBy(x => x).SequenceEqual(new[] { scene.Id, second.Id }.OrderBy(x => x)), "Thread-to-thread link was not mirrored.");
        project = await Update(c, first, Details("Race", "", "", DocumentStatus.Draft, 1000, []), project.Revision);
        Require(project.Document(scene.Id)!.Links.SequenceEqual([second.Id])
            && project.Document(second.Id)!.Links.SequenceEqual([scene.Id]), "Removing a link from one side left it on the other.");
        project = await Update(c, scene, Details("Arrival", "Updated", "", DocumentStatus.Draft, 1000), project.Revision);
        Require(project.Document(scene.Id)!.Links.Count == 1, "Omitting links removed them.");
        var manuscript = Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(manuscript))!;
        legacy["documents"]![scene.Id]!["characters"] = new JsonArray(mara.Id);
        legacy["documents"]![scene.Id]!["threads"] = new JsonArray(first.Id);
        legacy["threads"] = new JsonArray(first.Id);
        legacy["threadAxis"] = "columns";
        legacy["pinnedView"] = "threads";
        await File.WriteAllTextAsync(manuscript, legacy.ToJsonString());
        project = await c.Project();
        current = project.Document(scene.Id)!;
        Require(current.Links.OrderBy(x => x).SequenceEqual(new[] { second.Id, mara.Id, first.Id }.OrderBy(x => x)), "Legacy character and thread lists were not read as links.");
        Require(project.Document(mara.Id)!.Links.SequenceEqual([scene.Id]), "A one-sided legacy link should read back from the other side.");
        Require(FolderAt(project, "Manuscript").PinnedView == FolderView.Grid, "Legacy thread pin did not become the grid.");
        project = await Update(c, scene, Details("Arrival", "Updated again", "", DocumentStatus.Draft, 1000), project.Revision);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(manuscript))!;
        Require(written["documents"]![scene.Id]!["characters"] is null && written["documents"]![scene.Id]!["links"]!.AsArray().Count == 3 && written["threads"] is null && written["threadAxis"] is null,
            "Legacy keys were not folded into links or dropped on write.");
        var export = await c.Views.ExportAsync(project);
        Require(!export.Contains("Thread notes only") && export.Contains("Scene prose"), "Thread notes were included in manuscript export.");
        var characters = FolderAt(project, "Characters").Id;
        var layout = await c.Folders.SetLayoutAsync(c.Branch, FolderAt(project, "Manuscript").Id, new FolderLayout { GridFolderId = characters }, project.Revision);
        Require(layout.GridFolderId == characters, "Grid column folder was not saved.");
        project = await c.Project();
        layout = await c.Folders.SetLayoutAsync(c.Branch, layout.Id, new FolderLayout(), project.Revision);
        Require(layout.GridFolderId is null, "Grid column folder was not cleared.");
    }),
    ("Every folder gets a hidden document named after it, hidden from listings that expect emptiness and export", async c =>
    {
        var project = await c.Project();
        await c.Folders.CreateAsync(c.Branch, project.Root.Id, "Manuscript");
        project = await c.Project();
        await c.Folders.CreateAsync(c.Branch, FolderAt(project, "Manuscript").Id, "Chapter 09");
        project = await c.Project();
        var chapter = project.Documents.Single(d => d.Path == "Manuscript/Chapter 09/.Chapter 09.md");
        Require(chapter.Title == "Chapter 09" && chapter.WordGoal == 0 && chapter.Kind == DocumentKind.Scene && chapter.IsFolderDocument, "Folder document was not created with the folder's name.");
        Require(FolderAt(project, "Manuscript/Chapter 09").OwnDocument?.Id == chapter.Id && FolderAt(project, "Manuscript/Chapter 09").Children.Count == 0, "The folder's own document is reached through the folder, never listed as a child.");
        Require(File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 09", ".Chapter 09.md")), "Folder document is missing on disk.");
        Require(project.Documents.Any(d => d.Path == "Manuscript/.Manuscript.md") && project.Documents.All(d => d.Path != $".{c.Name}.md" && !d.Path.StartsWith('.')), "Top-level folders get documents; the project root does not.");
        Directory.CreateDirectory(Path.Combine(c.Root, "Manuscript", "Chapter 10"));
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 10", ".notes.md"), "ignored");
        project = await c.Project();
        Require(project.Documents.Any(d => d.Path == "Manuscript/Chapter 10/.Chapter 10.md") && project.Documents.All(d => !d.Path.EndsWith("/.notes.md")), "Dropped-in folder did not get its document, or another hidden file leaked in.");
        var scene = await c.Documents.CreateAsync(c.Branch, FolderAt(project, "Manuscript/Chapter 09").Id, "Arrival", "Scene prose");
        project = await c.Project();
        project = await Update(c, chapter, Details("Chapter 09", "Where it starts", "", DocumentStatus.Draft, 0, [scene.Id]), project.Revision);
        Require(project.Document(scene.Id)!.Links.SequenceEqual([chapter.Id]), "Folder documents link like any other.");
        var saved = await c.Documents.SaveBodyAsync(c.Branch, chapter.Id, "# Nine\n\nAn epigraph.", project.Document(chapter.Id)!.Revision);
        Require(saved.Body == "# Nine\n\nAn epigraph." && !(await c.Views.ExportAsync(await c.Project())).Contains("An epigraph."), "Folder document prose was not saved, or leaked into the export.");
        project = await c.Project();
        await c.Folders.CreateAsync(c.Branch, FolderAt(project, "Manuscript").Id, "Empty");
        project = await c.Project();
        await c.Folders.RemoveAsync(c.Branch, FolderAt(project, "Manuscript/Empty").Id, project.Revision);
        project = await c.Project();
        Require(project.FolderAt("Manuscript/Empty") is null && project.Documents.All(d => d.Path != "Manuscript/Empty/.Empty.md"), "A folder with only its own document could not be removed.");
        var plain = await c.Documents.CreateAsync(c.Branch, FolderAt(project, "Manuscript/Chapter 09").Id, ".Chapter 09", "");
        Require(PathOf(plain) == "Manuscript/Chapter 09/Chapter 09.md", "A title starting with a dot must not create a hidden file.");
    }),
    ("Migrates nested metadata into immediate-child manifests without rewriting prose", async c =>
    {
        var sceneId = Guid.NewGuid().ToString();
        var characterId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(c.Root, "Manuscript", "Chapter 01"));
        Directory.CreateDirectory(Path.Combine(c.Root, "Characters"));
        Directory.CreateDirectory(Path.Combine(c.Root, "Notes", "Empty"));
        Directory.CreateDirectory(Path.Combine(c.Root, ".odysseum"));
        var prose = $"---\nwriter_id: {sceneId}\n---\n\nUntouched prose.\n";
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", "Arrival.md"), prose);
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Characters", "Mara.md"), $"---\nwriter_id: {characterId}\n---\n\nBiography");
        var legacy = new JsonObject
        {
            ["version"] = 1, ["id"] = Guid.NewGuid().ToString(), ["title"] = "Legacy novel", ["custom"] = "retain root",
            ["documents"] = new JsonObject
            {
                [sceneId] = new JsonObject { ["path"] = "Manuscript/Chapter 01/Arrival.md", ["title"] = "A different title", ["synopsis"] = "Keep synopsis", ["order"] = 7, ["characters"] = new JsonArray(characterId), ["custom"] = "retain document" },
                [characterId] = new JsonObject { ["path"] = "Characters/Mara.md", ["title"] = "Mara", ["order"] = 9 }
            }
        }.ToJsonString();
        await File.WriteAllTextAsync(Path.Combine(c.Root, ".odysseum", "project.json"), legacy);
        var project = await c.Project();
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "project.json")))!;
        Require(manifest["version"]!.GetValue<int>() == 2 && manifest["documents"]!.AsObject().Count == 0, "Root still owns nested documents.");
        Require(manifest["folders"]!.AsObject().Count == 3 && manifest["custom"]!.GetValue<string>() == "retain root", "Root folders or extension properties were lost.");
        var chapter = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!;
        Require(chapter["documents"]![sceneId]!["path"]!.GetValue<string>() == "Arrival.md", "Chapter paths must be local filenames.");
        Require(chapter["documents"]![sceneId]!["custom"]!.GetValue<string>() == "retain document", "Document extension property was lost.");
        Require(chapter["documents"]![sceneId]!["order"] is null && chapter["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).Contains(sceneId), "The legacy order number was not folded into itemOrder.");
        Require(File.Exists(Path.Combine(c.Root, "Notes", "Empty", ".odysseum", "folder.json")), "Empty folders need manifests too.");
        var scene = project.Document(sceneId)!;
        Require(scene.Title == "A different title" && scene.Synopsis == "Keep synopsis" && scene.Links.SequenceEqual([characterId]), "Migration lost document metadata or links.");
        Require(Position(project, sceneId) < Position(project, characterId), "Migration lost the manuscript order.");
        Require(project.Document(characterId)!.Links.SequenceEqual([sceneId]), "A legacy one-sided link should read as undirected.");
        Require(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "project.v1.json")) == legacy, "Legacy backup is not exact.");
        Require(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", "Arrival.md")) == prose, "Migration rewrote prose.");
        Require((await c.Project()).Revision == project.Revision, "Unchanged scans must not rewrite manifests.");
    }),
    ("Folder moves retain identity and file moves transfer metadata ownership", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript/First")).Id, "Arrival", "Text");
        var before = await c.Project();
        await c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Arrival", "Keep me", "", DocumentStatus.Revised, 50), before.Revision);
        var oldFolder = Path.Combine(c.Root, "Manuscript", "First");
        var folderId = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(oldFolder, ".odysseum", "folder.json")))!["id"]!.GetValue<string>();
        Directory.Move(oldFolder, Path.Combine(c.Root, "Manuscript", "Renamed"));
        var renamed = await c.Documents.GetAsync(await c.Project(), scene.Id);
        Require(PathOf(renamed) == "Manuscript/Renamed/Arrival.md" && renamed.Synopsis == "Keep me", "Folder move lost metadata.");
        var parent = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json")))!;
        Require(parent["folders"]![folderId]!["path"]!.GetValue<string>() == "Renamed", "Parent did not track folder rename.");
        var moved = await c.Documents.MoveAsync(c.Branch, renamed.Id, (await Ensure(c, "Manuscript/Second")).Id, null, renamed.Revision);
        var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Renamed", ".odysseum", "folder.json")))!;
        var target = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Second", ".odysseum", "folder.json")))!;
        Require(source["documents"]!.AsObject().All(pair => pair.Value!["path"]!.GetValue<string>().StartsWith('.')) && target["documents"]![scene.Id] is not null && moved.Synopsis == "Keep me", "Move left duplicate metadata owners.");
        Require(target["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).Contains(scene.Id) && !source["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).Contains(scene.Id), "Move did not carry the document's place to the new folder.");
    }),
    ("Moving puts an item at an index in its folder or in another folder", async c =>
    {
        var manuscript = await Ensure(c, "Manuscript");
        var a = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "A", "");
        var b = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "B", "");
        var d = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "D", "");
        var notes = await Ensure(c, "Notes");
        var project = await c.Project();
        Require(FolderAt(project, "Manuscript").Children.Select(x => x.Id).SequenceEqual([a.Id, b.Id, d.Id]), "New documents are appended in order.");
        Require(project.Document(b.Id)!.ParentId == manuscript.Id && project.Document(b.Id)!.OrderInParent == 1, "Placed items know their parent and position.");
        await c.Folders.MoveAsync(c.Branch, d.Id, manuscript.Id, 0, project.Revision);
        project = await c.Project();
        Require(FolderAt(project, "Manuscript").Children.Select(x => x.Id).SequenceEqual([d.Id, a.Id, b.Id]), "Moving within a folder reorders its children.");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json")))!;
        Require(manifest["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual([d.Id, a.Id, b.Id]), "The order is stored in the folder's manifest.");
        await c.Folders.MoveAsync(c.Branch, a.Id, notes.Id, 0, project.Revision);
        project = await c.Project();
        Require(project.Document(a.Id)!.Path == "Notes/A.md" && File.Exists(Path.Combine(c.Root, "Notes", "A.md")) && !File.Exists(Path.Combine(c.Root, "Manuscript", "A.md")), "Moving to another folder moves the file.");
        Require(FolderAt(project, "Notes").Children.Select(x => x.Id).SequenceEqual([a.Id]) && FolderAt(project, "Manuscript").Children.Select(x => x.Id).SequenceEqual([d.Id, b.Id]), "Moving to another folder updates both folders' children.");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Notes", ".odysseum", "folder.json")))!["documents"]![a.Id] is not null, "The metadata moved to the new owner.");
        var chapter = await c.Folders.CreateAsync(c.Branch, project.Root.Id, "Chapter");
        project = await c.Project();
        await c.Folders.MoveAsync(c.Branch, chapter.Id, manuscript.Id, 1, project.Revision);
        project = await c.Project();
        Require(project.Folder(chapter.Id)?.Path == "Manuscript/Chapter" && Directory.Exists(Path.Combine(c.Root, "Manuscript", "Chapter")), "Moving a folder moves its directory and keeps its ID.");
        Require(FolderAt(project, "Manuscript").Children.Select(x => x.Id).SequenceEqual([d.Id, chapter.Id, b.Id]), "A moved folder takes the requested index.");
        await Expect(WorkspaceError.Invalid, () => c.Folders.MoveAsync(c.Branch, manuscript.Id, chapter.Id, 0, project.Revision));
        await Expect(WorkspaceError.Conflict, () => c.Folders.MoveAsync(c.Branch, b.Id, manuscript.Id, 0, "stale"));
    }),
    ("External folder metadata changes invalidate revisions and preserve extensions", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript/First")).Id, "Arrival", "Text");
        var before = await c.Project();
        var path = Path.Combine(c.Root, "Manuscript", "First", ".odysseum", "folder.json");
        var local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["custom"] = "folder extension";
        local["documents"]![scene.Id]!["synopsis"] = "From another editor";
        await File.WriteAllTextAsync(path, local.ToJsonString());
        await Expect(WorkspaceError.Conflict, () => c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Arrival", "Stale", "", DocumentStatus.Draft, 1), before.Revision));
        var current = await c.Project();
        Require(current.Documents.Where(Visible).Single().Synopsis == "From another editor" && current.Revision != before.Revision, "Folder edits were not observed.");
        await c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Arrival", "New", "", DocumentStatus.Done, 1), current.Revision);
        Require(JsonNode.Parse(await File.ReadAllTextAsync(path))!["custom"]!.GetValue<string>() == "folder extension", "Folder extension was lost.");
        local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["documents"]![scene.Id]!["path"] = "../escape.md";
        var invalid = local.ToJsonString();
        await File.WriteAllTextAsync(path, invalid);
        await Expect(WorkspaceError.Corrupt, () => c.Project());
        Require(await File.ReadAllTextAsync(path) == invalid, "Unsafe folder manifest was rewritten.");
    }),
    ("Replacing a filename with a new ID does not invalidate retained metadata", async c =>
    {
        var original = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "Original");
        var path = Path.Combine(c.Root, PathOf(original));
        var bytes = await File.ReadAllBytesAsync(path);
        var replacementId = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(path, $"---\nwriter_id: {replacementId}\n---\n\nReplacement");
        Require((await c.Project()).Documents.Where(Visible).Single().Id == replacementId, "Replacement retained the wrong identity.");
        Require((await c.Project()).Documents.Where(Visible).Single().Id == replacementId, "Retained metadata made the next scan invalid.");
        await File.WriteAllBytesAsync(path, bytes);
        Require((await c.Project()).Document(original.Id)!.Title == "Arrival", "Restoring the original lost its metadata.");
    }),
    ("Metadata directories from earlier releases are renamed when the project opens", async c =>
    {
        var legacyRoot = Path.Combine(c.Root, "Legacy");
        Directory.CreateDirectory(legacyRoot);
        await using (var first = new Workspace(c.Root))
        {
            var project = await first.ProjectAsync("Legacy");
            await first.Documents.CreateAsync(project.Branch, (await EnsureFolder(first, project, "Manuscript")).Id, "Arrival", "Keep prose");
            project = await first.ProjectAsync("Legacy");
            await first.Projects.SaveSettingsAsync(project.Branch, new ProjectSettings { Title = "Legacy title", WordGoal = 100 }, project.Revision);
        }
        foreach (var directory in new[] { legacyRoot, Path.Combine(legacyRoot, "Manuscript") })
            Directory.Move(Path.Combine(directory, ".odysseum"), Path.Combine(directory, ".writer"));
        await using var reopened = new Workspace(c.Root);
        var current = await reopened.ProjectAsync("Legacy");
        Require(current.Title == "Legacy title" && current.Documents.Where(Visible).Single().Title == "Arrival", "Legacy metadata was not carried over.");
        Require(Directory.Exists(Path.Combine(legacyRoot, "Manuscript", ".odysseum")) && !Directory.Exists(Path.Combine(legacyRoot, ".writer")), "Legacy metadata directories were not renamed.");
    }),
    ("Default folders can be removed when the server setting allows it", async c =>
    {
        Directory.CreateDirectory(Path.Combine(c.Root, "Permissive"));
        await using var permissive = new Workspace(c.Root, new AllowingSettings());
        var project = await permissive.ProjectAsync("Permissive");
        await permissive.Folders.CreateAsync(project.Branch, project.Root.Id, "Notes");
        project = await permissive.ProjectAsync("Permissive");
        await permissive.Folders.RemoveAsync(project.Branch, FolderAt(project, "Notes").Id, project.Revision);
        project = await permissive.ProjectAsync("Permissive");
        Require(project.FolderAt("Notes") is null, "The default folder should be removable when allowed.");
    }),
    ("An interrupted manifest batch rolls back on reopening", async c =>
    {
        var recoveryRoot = Path.Combine(c.Root, "Recovery");
        Directory.CreateDirectory(recoveryRoot);
        byte[] original;
        string folderPath;
        await using (var first = new Workspace(c.Root))
        {
            var project = await first.ProjectAsync("Recovery");
            await first.Documents.CreateAsync(project.Branch, (await EnsureFolder(first, project, "Manuscript")).Id, "Arrival", "Keep prose");
            folderPath = Path.Combine(recoveryRoot, "Manuscript", ".odysseum", "folder.json");
            original = await File.ReadAllBytesAsync(folderPath);
        }
        var backup = $".odysseum/manifest-transaction/{Guid.NewGuid():N}.bak";
        Directory.CreateDirectory(Path.Combine(recoveryRoot, ".odysseum", "manifest-transaction"));
        await File.WriteAllBytesAsync(Path.Combine(recoveryRoot, backup), original);
        var interrupted = JsonNode.Parse(original)!;
        interrupted["documents"]!.AsObject().First().Value!["title"] = "Uncommitted";
        var changed = Encoding.UTF8.GetBytes(interrupted.ToJsonString());
        await File.WriteAllBytesAsync(folderPath, changed);
        var journal = System.Text.Json.JsonSerializer.Serialize(new[] { new { Path = "Manuscript/.odysseum/folder.json", Backup = backup, After = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed)).ToLowerInvariant() } });
        await File.WriteAllTextAsync(Path.Combine(recoveryRoot, ".odysseum", "pending-manifests.json"), journal);
        await using var reopened = new Workspace(c.Root);
        Require((await reopened.ProjectAsync("Recovery")).Documents.Where(Visible).Single().Title == "Arrival", "Incomplete batch was treated as committed.");
        var restored = await File.ReadAllBytesAsync(folderPath);
        Require(original.SequenceEqual(restored), "Recovery did not restore the exact original manifest.");
        Require(!File.Exists(Path.Combine(recoveryRoot, ".odysseum", "pending-manifests.json")), "Recovery journal was not cleared.");
    }),
    ("Locations behave like characters and validate scene links", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "Scene prose");
        var place = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Locations/Coast")).Id, "Harbour", "Location research");
        var character = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "Biography");
        Require(place.Kind == DocumentKind.Location, "Nested location was classified as a scene.");
        var project = await c.Project();
        project = await Update(c, scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [character.Id, place.Id, place.Id]), project.Revision);
        Require(project.Document(scene.Id)!.Links.SequenceEqual([character.Id, place.Id]), "Links were not stored or deduplicated.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Rejected", "", "", DocumentStatus.Done, 1, [Guid.NewGuid().ToString()]), project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(c.Branch, scene.Id, Details("Rejected", "", "", DocumentStatus.Done, 1, Enumerable.Repeat(place.Id, 201).ToArray()), project.Revision));
        project = await Update(c, scene, Details("Arrival", "Changed", "", DocumentStatus.Draft, 1000), project.Revision);
        Require(project.Document(scene.Id)!.Links.SequenceEqual([character.Id, place.Id]), "Omitted links or rejected request cleared attachments.");
        Require(!(await c.Views.ExportAsync(project)).Contains("Location research"), "Locations leaked into manuscript export.");
        Require((await c.Views.SearchAsync(project, "Location research")).Single().Document.Id == place.Id, "Location prose is not searchable.");
    }),
    ("Versions keep the whole project, and a restore is itself a new version", async c =>
    {
        var scene = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Manuscript")).Id, "Arrival", "First draft");
        var external = Path.Combine(c.Root, "Notes", "crlf.md");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Line one\r\nLine two\r\n")];
        await File.WriteAllBytesAsync(external, bytes);
        var named = await c.W.HistoryService.SaveVersionAsync(c.Branch, "  Draft one ");
        Require(named.Label == "Draft one" && !named.Automatic, "A named version should carry its trimmed name.");
        await c.Documents.SaveBodyAsync(c.Branch, scene.Id, "Second draft", scene.Revision);
        await File.WriteAllTextAsync(external, "Rewritten\n");
        var extra = await c.Documents.CreateAsync(c.Branch, (await Ensure(c, "Characters")).Id, "Mara", "Biography");
        var automatic = await c.W.History.SaveVersionAsync(c.Branch, null);
        Require(automatic is { Automatic: true, Label: null, Changes: >= 3 }, "An automatic version records every changed file.");
        Require(await c.W.History.SaveVersionAsync(c.Branch, null) is null, "Nothing changed, so no version should be saved.");
        var versions = await c.W.HistoryService.ListVersionsAsync(c.Branch);
        Require(versions[0].Id == automatic!.Id && versions[1].Id == named.Id, "Versions list newest first.");
        Require((await File.ReadAllTextAsync(Path.Combine(c.Root, ".git", "info", "exclude"))).Contains("instance.lock"), "The instance lock must stay out of versions.");
        var restored = (Project)await c.W.HistoryService.RestoreAsync(c.Branch, named.Id);
        Require(restored.Document(extra.Id) is null && !File.Exists(Path.Combine(c.Root, PathOf(extra))), "A restore removes documents added since.");
        Require((await Open(c, scene.Id)).Body == "First draft", "A restore brings back the old prose.");
        Require((await File.ReadAllBytesAsync(external)).SequenceEqual(bytes), "Restored files must match byte for byte, BOM and CRLF included.");
        versions = await c.W.HistoryService.ListVersionsAsync(c.Branch);
        Require(versions[0].Label!.StartsWith("Restored") && versions[1].Id == automatic.Id, "A restore is a new version on top, not a rewind.");
        restored = (Project)await c.W.HistoryService.RestoreAsync(c.Branch, versions[1].Id);
        Require(restored.Document(extra.Id) is not null && (await Open(c, scene.Id)).Body == "Second draft", "Restoring the version before a restore undoes it.");
        await Expect(WorkspaceError.NotFound, () => c.W.HistoryService.RestoreAsync(c.Branch, "0123456789abcdef0123456789abcdef01234567"));
        await Expect(WorkspaceError.NotFound, () => c.W.HistoryService.RestoreAsync(c.Branch, "HEAD~1"));
        await Expect(WorkspaceError.Invalid, () => c.W.HistoryService.SaveVersionAsync(c.Branch, "  "));
    }),
    ("History lists and restores the versions of one document", async c =>
    {
        var manuscript = await Ensure(c, "Manuscript");
        var a = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "A", "A one");
        var b = await c.Documents.CreateAsync(c.Branch, manuscript.Id, "B", "B one");
        var first = await c.W.HistoryService.SaveVersionAsync(c.Branch, "First");
        await c.Documents.SaveBodyAsync(c.Branch, a.Id, "A two", a.Revision);
        var second = await c.W.HistoryService.SaveVersionAsync(c.Branch, "Second");
        var ofA = await c.W.HistoryService.ListVersionsAsync(c.Branch, a.Id);
        var ofB = await c.W.HistoryService.ListVersionsAsync(c.Branch, b.Id);
        Require(ofA.Select(v => v.Id).SequenceEqual([second.Id, first.Id]), "A document's history lists the versions that changed it, newest first.");
        Require(ofB.Select(v => v.Id).SequenceEqual([first.Id]), "A document's history leaves out versions that did not change it.");
        Require(await c.W.HistoryService.ReadAsync(c.Branch, first.Id, a.Id) == "A one", "An old version of a document can be read.");
        await Expect(WorkspaceError.NotFound, () => c.W.HistoryService.ReadAsync(c.Branch, first.Id, Guid.NewGuid().ToString()));
        var restored = (Project)await c.W.HistoryService.RestoreAsync(c.Branch, first.Id, a.Id);
        Require((await Open(c, a.Id)).Body == "A one", "Restoring one document brings back its old prose.");
        Require((await Open(c, b.Id)).Body == "B one", "Restoring one document leaves the others alone.");
        var versions = await c.W.HistoryService.ListVersionsAsync(c.Branch);
        Require(versions[0].Label!.StartsWith("Restored") && restored.Document(a.Id) is not null, "Restoring one document is itself a new version.");
    }),
    ("Branch operations are not supported yet", async c =>
    {
        await c.Project();
        try { await c.W.History.CreateBranchAsync(c.Branch, "draft"); throw new Exception("Creating a branch should not be supported yet."); }
        catch (NotSupportedException) { }
        try { await c.W.History.MergeAsync(new ProjectBranch(c.Name, "draft"), c.Branch); throw new Exception("Merging should not be supported yet."); }
        catch (NotSupportedException) { }
        await Expect(WorkspaceError.Invalid, () => c.W.History.ListVersionsAsync(new ProjectBranch(c.Name, "draft")));
        await Expect(WorkspaceError.Invalid, () => c.Projects.GetAsync(new ProjectBranch(c.Name, "draft")));
    }),
};

var libraryChecks = new List<(string Name, Func<string, Workspace, Task> Run)>
{
    ("Creates project folders with unique names and their own metadata", async (root, w) =>
    {
        Require((await w.Sessions.ListAsync()).Count == 0, "An empty workspace should list no projects.");
        var first = await w.Sessions.CreateAsync("My Novel", 80000);
        var second = await w.Sessions.CreateAsync("My Novel");
        var odd = await w.Sessions.CreateAsync("Draft: Part 1");
        Require(first.Name == "My Novel" && second.Name == "My Novel-2" && odd.Name == "Draft- Part 1", "Folder names were not derived safely.");
        Require(File.Exists(Path.Combine(root, "My Novel", ".odysseum", "project.json")), "The project folder has no metadata.");
        var view = await w.ProjectAsync("My Novel");
        Require(view.Title == "My Novel" && view.WordGoal == 80000, "Title or goal was not stored.");
        var oddProject = await w.ProjectAsync(odd.Name);
        await w.Documents.CreateAsync(oddProject.Branch, FolderAt(oddProject, "Manuscript").Id, "Scene", "Text");
        Require((await w.Sessions.ListAsync()).Select(x => x.Title).SequenceEqual(["Draft: Part 1", "My Novel", "My Novel"]), "Listing should show manifest titles, even with documents present.");
    }),
    ("New projects seed default folders in sidebar order and protect them from removal", async (root, w) =>
    {
        var created = await w.Sessions.CreateAsync("Seeded");
        var project = await w.ProjectAsync(created.Name);
        Require(Directory.Exists(Path.Combine(root, created.Name, "Manuscript", "Chapter 01")) && project.FolderAt("Manuscript/Chapter 01") is not null, "Chapter 01 was not seeded.");
        Require(project.Root.Folders.Select(f => f.Name).SequenceEqual(DefaultFolders.Names), "Default folders were not ordered.");
        var scene = project.Documents.Single(Scene);
        Require(scene.Path == "Manuscript/Chapter 01/Scene 01.md" && scene.Title == "Scene 01" && scene.WordGoal == 1000, "Scene 01 was not seeded.");
        await Expect(WorkspaceError.Forbidden, () => w.Folders.RemoveAsync(project.Branch, FolderAt(project, "Threads").Id, project.Revision));
        await Expect(WorkspaceError.Conflict, () => w.Folders.RemoveAsync(project.Branch, FolderAt(project, "Manuscript/Chapter 01").Id, project.Revision));
        Require((await w.HistoryService.ListVersionsAsync(project.Branch)).Count == 1, "A new project's first version is its template.");
    }),
    ("Project templates capture a project by path and seed new projects with fresh ids", async (root, _) =>
    {
        var templates = new TemplateRepository(Path.Combine(root, ".templates"));
        templates.EnsureDefault();
        Require(File.Exists(Path.Combine(root, ".templates", "Default.json")) && templates.List().Single().Name == "Default", "The Default template was not written.");
        await using var w = new Workspace(Path.Combine(root, "workspace"), templates: templates, watch: true);
        var sourceName = (await w.Sessions.CreateAsync("Source", 70000)).Name;
        var branch = ProjectBranch.Main(sourceName);
        var project = await w.ProjectAsync(sourceName);
        await w.Folders.CreateAsync(branch, FolderAt(project, "Manuscript").Id, "Chapter 02");
        project = await w.ProjectAsync(sourceName);
        var scene = await w.Documents.CreateAsync(branch, FolderAt(project, "Manuscript/Chapter 02").Id, "Opening", "Once.");
        var mara = await w.Documents.CreateAsync(branch, FolderAt(project, "Characters").Id, "Mara", "Sheet");
        project = await w.ProjectAsync(sourceName);
        await w.Documents.UpdateAsync(branch, scene.Id, Details("The Opening", "It begins.", "", DocumentStatus.Done, 250, [mara.Id],
            new() { [mara.Id] = "First sight" }), project.Revision);
        project = await w.ProjectAsync(sourceName);
        var characters = FolderAt(project, "Characters");
        var chapter2 = await w.Folders.SetLayoutAsync(branch, FolderAt(project, "Manuscript/Chapter 02").Id, new FolderLayout { PinnedView = FolderView.Board, GridFolderId = characters.Id }, project.Revision);
        project = await w.ProjectAsync(sourceName);
        await w.Folders.MoveAsync(branch, scene.Id, chapter2.Id, 0, project.Revision);

        var saved = templates.Save(w.Templates.Capture(await w.ProjectAsync(sourceName), "Novel"));
        Require(saved.Documents.Select(d => d.Path).SequenceEqual(["Manuscript/Chapter 01/Scene 01.md", "Manuscript/Chapter 02/Opening.md", "Characters/Mara.md", "Styles/Default.md"]),
            "The template should list documents in order and leave out folders' own documents.");
        var json = File.ReadAllText(Path.Combine(root, ".templates", "Novel.json"));
        Require(!json.Contains(scene.Id) && !json.Contains("Once.") && !json.Contains("It begins."), "A project template carries neither ids nor what documents hold.");

        var view = await w.ProjectAsync((await w.Sessions.CreateAsync("Copy", null, "Novel")).Name);
        var opening = view.Documents.Single(d => d.Path == "Manuscript/Chapter 02/Opening.md");
        var sheet = view.Documents.Single(d => d.Path == "Characters/Mara.md");
        Require(view.Title == "Copy" && view.WordGoal == 70000, "Template goals were not applied.");
        Require(opening.Id != scene.Id && sheet.Id != mara.Id, "Documents made from a template need their own ids.");
        Require(opening.Title == "The Opening" && opening.Synopsis == "" && opening.WordGoal == 1000 && opening.Status == DocumentStatus.Draft
            && opening.Links.Count == 0 && (await w.Documents.OpenAsync(view.Branch, opening.Id)).Body == "", "Documents made from a template should start empty under their title.");
        var chapter = FolderAt(view, "Manuscript/Chapter 02");
        Require(chapter.PinnedView == FolderView.Board && chapter.Children.Select(child => child.Id).SequenceEqual([opening.Id])
            && chapter.GridFolderId == FolderAt(view, "Characters").Id && chapter.GridFolderId != characters.Id, "Folder layouts were not rebuilt.");
        Require(view.Root.Folders.Select(f => f.Name).SequenceEqual(DefaultFolders.Names), "The root order was lost.");

        await Expect(WorkspaceError.NotFound, () => w.Sessions.CreateAsync("Orphan", null, "Missing"));
        Require(!Directory.Exists(Path.Combine(root, "workspace", "Orphan")), "A missing template left an empty project behind.");
        await File.WriteAllTextAsync(Path.Combine(root, ".templates", "Bad.json"), "{\"documents\":[{\"path\":\"../escape.md\"}]}");
        Require(templates.List().Select(t => t.Name).SequenceEqual(["Default", "Novel"]), "An unsafe template should be skipped.");
        ExpectSync(WorkspaceError.Invalid, () => templates.Save(new() { Name = "Unsafe", Documents = [new() { Path = ".odysseum/project.json" }] }));
        templates.Delete("Novel");
        ExpectSync(WorkspaceError.NotFound, () => templates.Get("Novel"));
        templates.Delete("Default");
        Require(File.Exists(Path.Combine(root, ".templates", "Default.json")), "Deleting Default should restore the shipped one.");
    }),
    ("Lists dropped-in folders and ignores files, hidden, and metadata directories", async (root, w) =>
    {
        Directory.CreateDirectory(Path.Combine(root, "Dropped in"));
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        await File.WriteAllTextAsync(Path.Combine(root, "stray.md"), "Not a project");
        Require((await w.Sessions.ListAsync()).Select(x => x.Name).SequenceEqual(["Dropped in"]), "Only real project folders should be listed.");
        var view = await w.ProjectAsync("Dropped in");
        Require(view.Title == "Dropped in", "The folder name should become the working title.");
        Require(File.Exists(Path.Combine(root, "Dropped in", ".odysseum", "project.json")), "Opening should create metadata.");
    }),
    ("Rejects unsafe project names and reports missing projects", async (_, w) =>
    {
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync("../outside"));
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync(".odysseum"));
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync("nested/name"));
        await Expect(WorkspaceError.Invalid, () => w.Sessions.CreateAsync("   "));
        await Expect(WorkspaceError.NotFound, () => w.Projects.GetAsync("Missing"));
    }),
    ("Keeps projects isolated and reuses one open project per folder", async (_, w) =>
    {
        await w.Sessions.CreateAsync("One");
        await w.Sessions.CreateAsync("Two");
        var one = await w.Sessions.OpenAsync(ProjectBranch.Main("One"));
        Require(ReferenceEquals(one, await w.Sessions.OpenAsync(ProjectBranch.Main("One"))), "A project should open once per process.");
        var project = await w.ProjectAsync("One");
        await w.Documents.CreateAsync(project.Branch, FolderAt(project, "Manuscript").Id, "Only here", "Text");
        Require((await w.ProjectAsync("Two")).Documents.Count(Scene) == 1 && (await w.ProjectAsync("One")).Documents.Count(Scene) == 2, "Documents leaked between projects.");
    }),
    ("Listing uses legacy manifest settings without writing and survives invalid metadata", async (root, w) =>
    {
        var directory = Path.Combine(root, "Legacy");
        Directory.CreateDirectory(Path.Combine(directory, ".odysseum"));
        var manifestPath = Path.Combine(directory, ".odysseum", "project.json");
        var id = Guid.NewGuid().ToString();
        var legacy = "{\"version\":1,\"id\":\"" + id + "\",\"title\":\"Legacy title\",\"wordGoal\":12345,\"documents\":{}}";
        await File.WriteAllTextAsync(manifestPath, legacy);
        var listed = (await w.Sessions.ListAsync()).Single();
        Require(listed.Title == "Legacy title" && listed.Id == id, "Listing did not understand legacy metadata.");
        Require(await File.ReadAllTextAsync(manifestPath) == legacy, "Listing rewrote project metadata.");
        await File.WriteAllTextAsync(manifestPath, "{ broken metadata");
        listed = (await w.Sessions.ListAsync()).Single();
        Require(listed.Title == "Legacy" && listed.Id == "", "Invalid metadata prevented listing the folder.");
        await Expect(WorkspaceError.Corrupt, () => w.Projects.GetAsync("Legacy"));
        await File.WriteAllTextAsync(manifestPath, legacy);
        var opened = await w.ProjectAsync("Legacy");
        Require(opened.Id == id, "A failed open leaked its instance lock or prevented retry.");
        Require((await w.Projects.GetAsync(id)).Id == id, "A project should also open by its UUID.");
    }),
    ("Own saves are not reported as external changes, but edits from outside are", async (root, w) =>
    {
        var created = await w.Sessions.CreateAsync("Watched");
        var branch = ProjectBranch.Main(created.Name);
        var reported = 0;
        w.Watcher.Changed += _ => Interlocked.Increment(ref reported);
        var project = await w.ProjectAsync(created.Name);
        var scene = project.Documents.Single(Scene);
        var saved = await w.Documents.SaveBodyAsync(branch, scene.Id, "Typed in the app", scene.Revision);
        await w.Folders.CreateAsync(branch, project.Root.Id, "Made in the app");
        await Task.Delay(1500);
        Require(Volatile.Read(ref reported) == 0, "The watcher must not report the server's own writes.");
        await File.AppendAllTextAsync(Path.Combine(root, created.Name, "Manuscript", "Chapter 01", "Scene 01.md"), "\nTyped outside.");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref reported) == 0 && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require(Volatile.Read(ref reported) >= 1, "The watcher must report an edit made outside the app.");
        var session = w.Sessions.Get(branch)!;
        while (session.Current.Document(scene.Id)?.Revision == saved.Revision && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require(session.Current.Document(scene.Id)?.Revision != saved.Revision, "The session must reload after an external edit.");
    }),
};

await using (var workspace = new Workspace(testRoot))
{
    foreach (var (name, check) in checks)
    {
        var projectName = passed.ToString();
        var root = Path.Combine(testRoot, projectName);
        Directory.CreateDirectory(root);
        await check(new Check(root, projectName, workspace));
        passed++;
        Console.WriteLine($"PASS {name}");
    }
}
foreach (var (name, check) in libraryChecks)
{
    var root = Path.Combine(testRoot, "library-" + passed);
    await using var workspace = new Workspace(root, watch: true);
    await check(root, workspace);
    passed++;
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"\n{passed} storage checks passed. Fixtures: {testRoot}");

static bool Visible(Document document) => !document.IsFolderDocument;
static bool Scene(Document document) => Visible(document) && document.Kind == DocumentKind.Scene;
static string PathOf(IDocument document) => ((Document)document).Path;
static Folder FolderAt(IProject project, string path) => ((Project)project).FolderAt(path) ?? throw new Exception($"There is no folder at '{path}'.");
static Task<Folder> EnsureFolder(Workspace workspace, IProject project, string path) => FolderPaths.EnsureAsync(workspace.Folders, project, path);
static async Task<Folder> Ensure(Check check, string path) => await EnsureFolder(check.W, await check.Project(), path);
static Task<IDocument> Open(Check check, string id) => check.Documents.OpenAsync(check.Branch, id);
static async Task<Project> Update(Check check, IDocument document, DocumentDetails details, string revision)
{
    await check.Documents.UpdateAsync(check.Branch, document.Id, details, revision);
    return await check.Project();
}
static int Position(Project project, string id) =>
    project.Walk().Select((document, index) => (document, index)).Single(pair => pair.document.Id == id).index;
static DocumentDetails Details(string title, string synopsis, string notes, DocumentStatus status, int wordGoal, IReadOnlyList<string>? links = null, Dictionary<string, string>? linkNotes = null) =>
    new() { Title = title, Synopsis = synopsis, Notes = notes, Status = status, WordGoal = wordGoal, Links = links, LinkNotes = linkNotes };
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void ExpectSync(WorkspaceError error, Action action)
{
    try { action(); }
    catch (WorkspaceException ex) when (ex.Error == error) { return; }
    throw new Exception($"Expected a {error} rejection.");
}
static async Task Expect(WorkspaceError error, Func<Task> action)
{
    try { await action(); }
    catch (WorkspaceException ex) when (ex.Error == error) { return; }
    throw new Exception($"Expected a {error} rejection.");
}

sealed class Workspace : IAsyncDisposable
{
    public Workspace(string root, ISettingsProvider? settings = null, ITemplateRepository? templates = null, bool watch = false)
    {
        OwnWrites = new OwnWrites();
        Repository = new DiskProjectRepository(root, OwnWrites);
        History = new GitProjectHistory(root);
        Watcher = watch ? new FileProjectWatcher(root, OwnWrites, 300) : new NullProjectWatcher();
        Sessions = new ProjectSessions(Repository, Watcher, History, templates);
        Projects = new ProjectService(Sessions);
        Folders = new FolderService(Sessions, settings);
        Documents = new DocumentService(Sessions);
        HistoryService = new HistoryService(Sessions, History, 60);
        if (watch) HistoryService.Start();
        Views = new ProjectViews(Documents);
        Templates = new ProjectTemplateService();
    }

    public OwnWrites OwnWrites { get; }
    public IProjectRepository Repository { get; }
    public IProjectHistory History { get; }
    public IProjectWatcher Watcher { get; }
    public ProjectSessions Sessions { get; }
    public IProjectService Projects { get; }
    public IFolderService Folders { get; }
    public IDocumentService Documents { get; }
    public HistoryService HistoryService { get; }
    public ProjectViews Views { get; }
    public ProjectTemplateService Templates { get; }

    public async Task<Project> ProjectAsync(string name) => (Project)await Projects.GetAsync(name);

    public async ValueTask DisposeAsync()
    {
        await HistoryService.DisposeAsync();
        await Sessions.DisposeAsync();
        await Watcher.DisposeAsync();
        History.Dispose();
    }
}

sealed class NullProjectWatcher : IProjectWatcher
{
    public event Action<ProjectBranch>? Changed { add { } remove { } }
    public void Watch(ProjectBranch branch) { }
    public void Unwatch(ProjectBranch branch) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class Check(string root, string name, Workspace workspace)
{
    public string Root => root;
    public string Name => name;
    public ProjectBranch Branch => ProjectBranch.Main(name);
    public Workspace W => workspace;
    public IProjectService Projects => workspace.Projects;
    public IFolderService Folders => workspace.Folders;
    public IDocumentService Documents => workspace.Documents;
    public ProjectViews Views => workspace.Views;
    public Task<Project> Project() => workspace.ProjectAsync(name);
}

sealed class AllowingSettings : ISettingsProvider
{
    public IServerSettings GetSettings(bool copy = false) => new ServerSettings { AllowDeletingDefaultFolders = true };
    public void SaveSettings(IServerSettings settings) { }
    public void SaveSettings() { }
    public void DebugSettingsToLog() { }
}
