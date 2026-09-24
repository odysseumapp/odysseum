using System.Text;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Views;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<Check, Task> Run)>
{
    ("Folders retain layouts and identity through scans and external moves", async c =>
    {
        var project = await c.Project();
        var topics = await c.Folders.CreateAsync(project.RootFolder, "Topics");
        var race = await c.Folders.CreateAsync(topics, "Race");
        var doc = await c.Documents.CreateAsync(race, "Revelation", "Text");
        await c.Documents.CreateAsync(await Ensure(c, "Threads"), "Race", "Thread notes");
        project = await c.Project();
        var threads = FolderAt(project, "Threads");
        race = await c.Folders.SetLayoutAsync(FolderAt(project, "Topics/Race"), new FolderLayout { PinnedView = FolderView.Grid, GridFolderId = threads.Id }, project.Revision);
        await c.Order.ArrangeChildrenAsync(race, [doc.Id], race.Project.Revision);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Topics/Race/.odysseum/folder.json")))!;
        Require(manifest["pinnedView"]!.GetValue<string>() == "grid", "Pin was not stored in the owning folder.");
        Require(manifest["itemOrder"]![0]!.GetValue<string>() == doc.Id, "Child order was not stored in the owning folder.");
        Directory.Move(Path.Combine(c.Root, "Topics/Race"), Path.Combine(c.Root, "Topics/Class"));
        var moved = FolderAt(await c.Project(), "Topics/Class");
        Require(moved.Id == race.Id && moved.PinnedView == FolderView.Grid && moved.GridFolder?.Id == threads.Id, "Folder layout or identity was lost on move.");
    }),
    ("Folder removal refuses content and archives empty folder metadata", async c =>
    {
        var project = await c.Project();
        var empty = await c.Folders.CreateAsync(project.RootFolder, "Empty");
        var id = empty.Id;
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Empty/.keep"), "Keep this hidden file");
        await Expect(WorkspaceError.Conflict, () => c.Folders.RemoveAsync(empty, empty.Project.Revision));
        File.Delete(Path.Combine(c.Root, "Empty/.keep"));
        project = await c.Project();
        await c.Folders.RemoveAsync(FolderAt(project, "Empty"), project.Revision);
        project = await c.Project();
        Require(project.FolderAt("Empty") is null && !Directory.Exists(Path.Combine(c.Root, "Empty")), "Empty folder remains visible.");
        var archived = Directory.GetFiles(Path.Combine(c.Root, ".odysseum/removed-folders"), "folder.json", SearchOption.AllDirectories);
        Require(archived.Length == 1 && (await File.ReadAllTextAsync(archived[0])).Contains(id), "Removed metadata was lost.");
        await Expect(WorkspaceError.Invalid, () => c.Folders.RemoveAsync(project.RootFolder, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateAsync(project.RootFolder, "../Outside"));
    }),
    ("Folder layouts validate immediate children and reject stale writes", async c =>
    {
        var first = await c.Documents.CreateAsync(await Ensure(c, "One"), "First", "");
        var other = await c.Documents.CreateAsync(await Ensure(c, "Two"), "Other", "");
        var project = await c.Project();
        var one = FolderAt(project, "One");
        var two = FolderAt(project, "Two");
        await Expect(WorkspaceError.Invalid, () => c.Order.ArrangeChildrenAsync(one, [other.Id], project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(one, new FolderLayout { PinnedView = FolderView.Board, GridFolderId = Guid.NewGuid().ToString() }, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(one, new FolderLayout { PinnedView = FolderView.Board, GridFolderId = first.Id }, project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Folders.SetLayoutAsync(one, new FolderLayout { PinnedView = (FolderView)99 }, project.Revision));
        var root = await c.Folders.SetLayoutAsync(project.RootFolder, new FolderLayout { PinnedView = FolderView.Outline, GridFolderId = two.Id }, project.Revision);
        await c.Order.ArrangeChildrenAsync(root, [two.Id, one.Id], root.Project.Revision);
        project = await c.Project();
        var stale = project.Revision;
        await c.Folders.CreateAsync(project.RootFolder, "Three");
        await Expect(WorkspaceError.Conflict, () => c.Folders.SetLayoutAsync(project.RootFolder, new FolderLayout { PinnedView = FolderView.Board }, stale));
        project = await c.Project();
        Require(project.RootFolder.GridFolder?.Id == two.Id, "Grid column folder was lost.");
        Require((await c.Order.FoldersAsync(project.RootFolder)).Select(f => f.Id).SequenceEqual([two.Id, one.Id, FolderAt(project, "Three").Id]), "Root folder order was lost.");
        await Expect(WorkspaceError.Conflict, () => c.Folders.RemoveAsync(FolderAt(project, "One"), project.Revision));
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
        var doc = await c.Documents.GetAsync(await c.Project(), id);
        Require(doc.Body == body, "Frontmatter was not split exactly.");
        await c.Documents.SaveBodyAsync(doc, body + "Next paragraph.\r\n", doc.Revision);
        Require(await File.ReadAllTextAsync(path) == prefix.TrimStart('\uFEFF') + body + "Next paragraph.\r\n", "File formatting was changed.");
        Require((await File.ReadAllBytesAsync(path)).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "BOM was lost.");
    }),
    ("Rejects a stale browser save after an external edit with unchanged timestamps", async c =>
    {
        var doc = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Conflict", "Original text");
        var path = Path.Combine(c.Root, PathOf(doc));
        var timestamp = File.GetLastWriteTimeUtc(path);
        var raw = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, raw.Replace("Original text", "External text"));
        File.SetLastWriteTimeUtc(path, timestamp);
        await Expect(WorkspaceError.Conflict, () => c.Documents.SaveBodyAsync(doc, "Browser text", doc.Revision));
        Require((await File.ReadAllTextAsync(path)).Contains("External text"), "External edits were overwritten.");
    }),
    ("Serializes simultaneous browser saves so exactly one succeeds", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Two tabs", "Start");
        async Task<bool> Save(string text)
        {
            try { await c.Documents.SaveBodyAsync(doc, text, doc.Revision); return true; }
            catch (WorkspaceException ex) when (ex.Error == WorkspaceError.Conflict) { return false; }
        }
        var results = await Task.WhenAll(Save("First tab"), Save("Second tab"));
        Require(results.Count(x => x) == 1, "Expected one success and one conflict.");
    }),
    ("Serializes competing settings and document metadata updates", async c =>
    {
        var doc = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Original", "Text");
        var before = await c.Project();
        async Task<bool> Attempt(Func<Task> change)
        {
            try { await change(); return true; }
            catch (WorkspaceException ex) when (ex.Error == WorkspaceError.Conflict) { return false; }
        }
        var results = await Task.WhenAll(
            Attempt(() => c.Projects.SaveSettingsAsync(before, new ProjectSettings { Title = "Updated project", WordGoal = 12345 }, before.Revision)),
            Attempt(() => c.Documents.UpdateAsync(doc, Details("Updated document", "", "", DocumentStatus.Revised, 500), before.Revision)));
        Require(results.Count(success => success) == 1, "Expected exactly one metadata revision to succeed.");
        var after = await c.Project();
        Require(after.Title == (results[0] ? "Updated project" : before.Title), "Settings did not match the successful update.");
        Require(after.Documents.Where(Visible).Single().Title == (results[1] ? "Updated document" : "Original"), "Rejected document metadata leaked into state.");
    }),
    ("Retains metadata through an external move and content edit", async c =>
    {
        var doc = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "A scene", "Before");
        var project = await c.Project();
        await c.Documents.UpdateAsync(doc, Details("A better title", "A synopsis", "Private notes", DocumentStatus.Revised, 900), project.Revision);
        var oldPath = Path.Combine(c.Root, PathOf(doc));
        var target = Path.Combine(c.Root, "Other", "renamed.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(oldPath, target);
        await File.AppendAllTextAsync(target, "\nExternal addition.");
        var moved = await c.Documents.GetAsync(await c.Project(), doc.Id);
        Require(PathOf(moved) == "Other/renamed.md" && moved.Synopsis == "A synopsis" && moved.Title == "A better title", "Identity or metadata was lost.");
        Require(moved.Body.Contains("External addition"), "External edit was missed.");
    }),
    ("Does not recreate externally deleted documents", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Deleted", "Before");
        var path = Path.Combine(c.Root, PathOf(doc));
        File.Delete(path);
        await Expect(WorkspaceError.NotFound, () => c.Documents.SaveBodyAsync(doc, "Unsaved", doc.Revision));
        Require(!File.Exists(path), "A deleted file was recreated.");
    }),
    ("Preserves both revisions in readable snapshots", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "History", "First words");
        await c.Documents.SaveBodyAsync(doc, "Second words", doc.Revision);
        var snapshots = await c.Documents.ListVersionsAsync(doc);
        Require(snapshots.Count == 2, "Both sides of the save must be recoverable.");
        var contents = new List<string>();
        foreach (var snapshot in snapshots) contents.Add(await snapshot.ReadBodyAsync());
        Require(contents.Contains("First words") && contents.Contains("Second words"), "Missing revision content.");
    }),
    ("Checks metadata revisions and preserves unknown JSON fields", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Metadata", "Text");
        var oldProject = await c.Project();
        var path = Path.Combine(c.Root, ".odysseum", "project.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["customTool"] = "keep me";
        json["documents"]![doc.Id]!["customField"] = 42;
        json["settings"]!["title"] = "External project name";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Expect(WorkspaceError.Conflict, () => c.Projects.SaveSettingsAsync(oldProject, new ProjectSettings { Title = "Stale browser title", WordGoal = 100 }, oldProject.Revision));
        var current = await c.Project();
        await c.Projects.SaveSettingsAsync(current, new ProjectSettings { Title = "Updated title", WordGoal = 200 }, current.Revision);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Require(saved["customTool"]!.GetValue<string>() == "keep me" && saved["documents"]![doc.Id]!["customField"]!.GetValue<int>() == 42, "Unknown fields were discarded.");
    }),
    ("Refuses malformed metadata without overwriting it", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Protected", "Keep this");
        var path = Path.Combine(c.Root, ".odysseum", "project.json");
        await File.WriteAllTextAsync(path, "{ broken external edit");
        await Expect(WorkspaceError.Corrupt, () => c.Documents.SaveBodyAsync(doc, "New text", doc.Revision));
        Require(await File.ReadAllTextAsync(path) == "{ broken external edit", "Malformed metadata was replaced.");
    }),
    ("Blocks traversal and hidden internal paths", async c =>
    {
        var project = await c.Project();
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, "../outside"));
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, ".odysseum"));
        await Expect(WorkspaceError.Invalid, () => FolderPaths.EnsureAsync(c.Folders, project, "C:/outside"));
        await Expect(WorkspaceError.Invalid, () => c.Folders.CreateAsync(project.RootFolder, ".hidden"));
        ExpectSync(WorkspaceError.Invalid, () => FolderPaths.Split("../escape.md"));
        var doc = await c.Documents.CreateAsync(project.RootFolder, "Safe", "Text");
        var moved = await c.Documents.MoveAsync(doc, project.RootFolder, "../escape", doc.Revision);
        Require(PathOf(moved) == "escape.md", "A traversal attempt in a file name was not neutralised.");
    }),
    ("Rejects copied document IDs without confusing their content", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Original", "Keep");
        File.Copy(Path.Combine(c.Root, PathOf(doc)), Path.Combine(c.Root, "copied.md"));
        await Expect(WorkspaceError.Conflict, () => c.Project());
        Require((await File.ReadAllTextAsync(Path.Combine(c.Root, "copied.md"))).Contains("Keep"), "Copy was modified.");
    }),
    ("Exports manuscript order and excludes story notes", async c =>
    {
        var first = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "First", "First body");
        var second = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Second", "Second body");
        var note = await c.Documents.CreateAsync(await Ensure(c, "Notes"), "Secret notes", "Research only");
        var project = await c.Project();
        await c.Order.ArrangeDocumentsAsync(project, [second.Id, first.Id, note.Id], project.Revision);
        var export = await c.Views.ExportAsync(await c.Project());
        Require(export.IndexOf("Second body", StringComparison.Ordinal) < export.IndexOf("First body", StringComparison.Ordinal), "Export order is wrong.");
        Require(!export.Contains("Research only") && !export.Contains("writer_id"), "Export included internal metadata or notes.");
    }),
    ("Searches prose, synopsis, and author notes", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Find me", "The cartographer left at dawn.");
        var project = await c.Project();
        await c.Documents.UpdateAsync(doc, Details("Find me", "A lighthouse", "Remember Bellwether", DocumentStatus.Draft, 500), project.Revision);
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
        await Expect(WorkspaceError.Invalid, () => c.Projects.SaveSettingsAsync(project, new ProjectSettings { Title = " ", WordGoal = 1 }, project.Revision));
        var updated = await c.Projects.SaveSettingsAsync(project, new ProjectSettings { Title = "Legacy title", WordGoal = 12345, DefaultSceneWordGoal = 700 }, project.Revision);
        var doc = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Fresh scene", "Text");
        Require(doc.WordGoal == 700 && updated.DefaultSceneWordGoal == 700, "New scenes should take the project's default goal.");
    }),
    ("Classifies documents by folder and attaches characters to scenes", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "Text");
        var character = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "A cartographer.");
        var note = await c.Documents.CreateAsync(await Ensure(c, "Notes"), "Island", "Research");
        Require(scene.Kind == DocumentKind.Scene && character.Kind == DocumentKind.Character && note.Kind == DocumentKind.Note, "Kinds were not derived from folders.");
        var project = await c.Project();
        var updated = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [character.Id]), project.Revision));
        Require(updated.Document(scene.Id)!.LinkIds.SequenceEqual([character.Id]), "Attached characters were not stored.");
        Require(updated.Document(character.Id)!.LinkIds.SequenceEqual([scene.Id]), "Links are undirected: the character should list the scene.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [scene.Id]), updated.Revision));
        Require(!(await c.Views.ExportAsync(await c.Project())).Contains("A cartographer."), "Characters must not appear in the manuscript export.");
    }),
    ("An invalid metadata request leaves existing details unchanged", async c =>
    {
        var doc = await c.Documents.CreateAsync((await c.Project()).RootFolder, "Keep title", "Text");
        var project = await c.Project();
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(doc, Details("Wrong title", "", "", (DocumentStatus)99, 5), project.Revision));
        Require((await c.Documents.GetAsync(await c.Project(), doc.Id)).Title == "Keep title", "Rejected request partially changed metadata.");
    }),
    ("Rejected character attachments never persist other metadata changes", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Keep title", "Text");
        var character = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "Biography");
        var project = await c.Project();
        project = Snapshot(await c.Documents.UpdateAsync(scene,
            Details("Keep title", "Keep synopsis", "Keep notes", DocumentStatus.Draft, 1000, [character.Id]), project.Revision));
        var manifestPath = Path.Combine(c.Root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        foreach (var attachments in new[] { new[] { scene.Id }, Enumerable.Repeat(character.Id, 201).ToArray() })
        {
            await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(scene,
                Details("Rejected title", "Rejected synopsis", "Rejected notes", DocumentStatus.Revised, 5, attachments), project.Revision));
            var current = await c.Project();
            var details = current.Document(scene.Id)!;
            Require(details.Title == "Keep title" && details.Synopsis == "Keep synopsis" && details.Notes == "Keep notes"
                && details.Status == DocumentStatus.Draft && details.WordGoal == 1000
                && details.LinkIds.SequenceEqual([character.Id]), "Rejected request changed the in-memory metadata.");
            var persisted = await File.ReadAllBytesAsync(manifestPath);
            Require(current.Revision == project.Revision && original.SequenceEqual(persisted),
                "A later scan persisted a rejected request.");
        }
    }),
    ("Failed scans do not publish partially discovered metadata", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Original", "Keep this");
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
        var first = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "First", "Text");
        await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Second", "Text");
        var before = await c.Project();
        var manifestPath = Path.Combine(c.Root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        Func<Task>[] changes =
        [
            () => c.Documents.UpdateAsync(first, Details("Rejected", "Synopsis", "Notes", DocumentStatus.Revised, 5), before.Revision),
            () => c.Projects.SaveSettingsAsync(before, new ProjectSettings { Title = "Rejected settings", WordGoal = 5 }, before.Revision),
            () => c.Order.ArrangeDocumentsAsync(before, before.Documents.Select(x => x.Id).Reverse().ToArray(), before.Revision),
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
        var doc = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Keep", "Text");
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
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "Scene prose");
        var thread = await c.Documents.CreateAsync(await Ensure(c, "Threads"), "Race", "Thread notes");
        var other = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "Biography");
        var project = await c.Project();
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [thread.Id],
            new() { [thread.Id] = "  Mara first doubts the map  ", [other.Id] = "Not linked" }), project.Revision));
        var notes = project.Document(scene.Id)!.LinkNotes;
        Require(notes.Count == 1 && notes[thread.Id] == "Mara first doubts the map", "The note was not stored trimmed, or a note without a link was kept.");
        Require(project.Document(thread.Id)!.LinkNotes[scene.Id] == "Mara first doubts the map", "The other end should read the same note.");
        var threadManifest = Path.Combine(c.Root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![thread.Id]!["linkNotes"]![scene.Id]!.GetValue<string>() == "Mara first doubts the map", "The note was not persisted on the other side.");
        project = Snapshot(await c.Documents.UpdateAsync(thread, Details("Race", "", "", DocumentStatus.Draft, 1000, null, new() { [scene.Id] = "Rewritten" }), project.Revision));
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "Synopsis", "", DocumentStatus.Draft, 1000), project.Revision));
        Require(project.Document(scene.Id)!.LinkNotes[thread.Id] == "Rewritten", "A note edited from the other end did not change here, or an unrelated save lost it.");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!;
        manifest["documents"]![thread.Id]!["linkNotes"] = new JsonObject();
        await File.WriteAllTextAsync(threadManifest, manifest.ToJsonString());
        project = await c.Project();
        Require(project.Document(thread.Id)!.LinkNotes[scene.Id] == "Rewritten", "A one-sided note should read from the other end.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new() { [thread.Id] = new string('x', 2001) }), project.Revision));
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new()), project.Revision));
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "An empty note set should clear the note on both sides.");
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, null, new() { [thread.Id] = "Back" }), project.Revision));
        project = Snapshot(await c.Documents.UpdateAsync(thread, Details("Race", "", "", DocumentStatus.Draft, 1000, []), project.Revision));
        project = Snapshot(await c.Documents.UpdateAsync(thread, Details("Race", "", "", DocumentStatus.Draft, 1000, [scene.Id]), project.Revision));
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "Unlinking should take the note with it on both sides.");
    }),
    ("Links are undirected, kept on both sides, and legacy character, location and thread lists migrate", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "Scene prose");
        var first = await c.Documents.CreateAsync(await Ensure(c, "Threads"), "Race", "Thread notes only");
        var second = await c.Documents.CreateAsync(await Ensure(c, "Threads/Story Beats"), "Meet Cute", "A nested thread");
        var mara = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "Biography");
        Require(first.Kind == DocumentKind.Thread && second.Kind == DocumentKind.Thread, "Thread file was classified as a scene.");
        var project = await c.Project();
        var positionBefore = await Position(c, project, scene.Id);
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [first.Id, second.Id]), project.Revision));
        var current = project.Document(scene.Id)!;
        Require(current.LinkIds.SequenceEqual(new[] { first.Id, second.Id }) && await Position(c, project, scene.Id) == positionBefore, "Linking changed manuscript order or lost a link.");
        Require(project.Document(first.Id)!.LinkIds.SequenceEqual([scene.Id]), "The thread should list the scene back.");
        var threadManifest = Path.Combine(c.Root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![first.Id]!["links"]![0]!.GetValue<string>() == scene.Id, "The reverse side was not persisted.");
        project = Snapshot(await c.Documents.UpdateAsync(second, Details("Meet Cute", "", "", DocumentStatus.Draft, 1000, [scene.Id, first.Id]), project.Revision));
        Require(project.Document(first.Id)!.LinkIds.OrderBy(x => x).SequenceEqual(new[] { scene.Id, second.Id }.OrderBy(x => x)), "Thread-to-thread link was not mirrored.");
        project = Snapshot(await c.Documents.UpdateAsync(first, Details("Race", "", "", DocumentStatus.Draft, 1000, []), project.Revision));
        Require(project.Document(scene.Id)!.LinkIds.SequenceEqual([second.Id])
            && project.Document(second.Id)!.LinkIds.SequenceEqual([scene.Id]), "Removing a link from one side left it on the other.");
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "Updated", "", DocumentStatus.Draft, 1000), project.Revision));
        Require(project.Document(scene.Id)!.LinkIds.Count == 1, "Omitting links removed them.");
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
        Require(current.LinkIds.OrderBy(x => x).SequenceEqual(new[] { second.Id, mara.Id, first.Id }.OrderBy(x => x)), "Legacy character and thread lists were not read as links.");
        Require(project.Document(mara.Id)!.LinkIds.SequenceEqual([scene.Id]), "A one-sided legacy link should read back from the other side.");
        Require(FolderAt(project, "Manuscript").PinnedView == FolderView.Grid, "Legacy thread pin did not become the grid.");
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "Updated again", "", DocumentStatus.Draft, 1000), project.Revision));
        var written = JsonNode.Parse(await File.ReadAllTextAsync(manuscript))!;
        Require(written["documents"]![scene.Id]!["characters"] is null && written["documents"]![scene.Id]!["links"]!.AsArray().Count == 3 && written["threads"] is null && written["threadAxis"] is null,
            "Legacy keys were not folded into links or dropped on write.");
        var export = await c.Views.ExportAsync(project);
        Require(!export.Contains("Thread notes only") && export.Contains("Scene prose"), "Thread notes were included in manuscript export.");
        var characters = FolderAt(project, "Characters").Id;
        var layout = await c.Folders.SetLayoutAsync(FolderAt(project, "Manuscript"), new FolderLayout { GridFolderId = characters }, project.Revision);
        Require(layout.GridFolder?.Id == characters, "Grid column folder was not saved.");
        layout = await c.Folders.SetLayoutAsync(layout, new FolderLayout(), layout.Project.Revision);
        Require(layout.GridFolder is null, "Grid column folder was not cleared.");
    }),
    ("Every folder gets a hidden document named after it, hidden from listings that expect emptiness and export", async c =>
    {
        var project = await c.Project();
        await c.Folders.CreateAsync(project.RootFolder, "Manuscript");
        project = await c.Project();
        await c.Folders.CreateAsync(FolderAt(project, "Manuscript"), "Chapter 09");
        project = await c.Project();
        var chapter = project.Documents.Single(d => d.Path == "Manuscript/Chapter 09/.Chapter 09.md");
        Require(chapter.Title == "Chapter 09" && chapter.WordGoal == 0 && chapter.Kind == DocumentKind.Scene, "Folder document was not created with the folder's name.");
        Require(File.Exists(Path.Combine(c.Root, "Manuscript", "Chapter 09", ".Chapter 09.md")), "Folder document is missing on disk.");
        Require(project.Documents.Any(d => d.Path == "Manuscript/.Manuscript.md") && project.Documents.All(d => d.Path != $".{c.Slug}.md" && !d.Path.StartsWith('.')), "Top-level folders get documents; the project root does not.");
        Directory.CreateDirectory(Path.Combine(c.Root, "Manuscript", "Chapter 10"));
        await File.WriteAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 10", ".notes.md"), "ignored");
        project = await c.Project();
        Require(project.Documents.Any(d => d.Path == "Manuscript/Chapter 10/.Chapter 10.md") && project.Documents.All(d => !d.Path.EndsWith("/.notes.md")), "Dropped-in folder did not get its document, or another hidden file leaked in.");
        var scene = await c.Documents.CreateAsync(FolderAt(project, "Manuscript/Chapter 09"), "Arrival", "Scene prose");
        project = await c.Project();
        project = Snapshot(await c.Documents.UpdateAsync(chapter, Details("Chapter 09", "Where it starts", "", DocumentStatus.Draft, 0, [scene.Id]), project.Revision));
        Require(project.Document(scene.Id)!.LinkIds.SequenceEqual([chapter.Id]), "Folder documents link like any other.");
        var saved = await c.Documents.SaveBodyAsync(chapter, "# Nine\n\nAn epigraph.", (await c.Documents.GetAsync(project, chapter.Id)).Revision);
        Require(saved.Body == "# Nine\n\nAn epigraph." && !(await c.Views.ExportAsync(await c.Project())).Contains("An epigraph."), "Folder document prose was not saved, or leaked into the export.");
        project = await c.Project();
        await c.Folders.CreateAsync(FolderAt(project, "Manuscript"), "Empty");
        project = await c.Project();
        await c.Folders.RemoveAsync(FolderAt(project, "Manuscript/Empty"), project.Revision);
        project = await c.Project();
        Require(project.FolderAt("Manuscript/Empty") is null && project.Documents.All(d => d.Path != "Manuscript/Empty/.Empty.md"), "A folder with only its own document could not be removed.");
        var plain = await c.Documents.CreateAsync(FolderAt(project, "Manuscript/Chapter 09"), ".Chapter 09", "");
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
        Require(scene.Title == "A different title" && scene.Synopsis == "Keep synopsis" && scene.LinkIds.SequenceEqual([characterId]), "Migration lost document metadata or links.");
        Require(await Position(c, project, sceneId) < await Position(c, project, characterId), "Migration lost the manuscript order.");
        Require(project.Document(characterId)!.LinkIds.SequenceEqual([sceneId]), "A legacy one-sided link should read as undirected.");
        Require(await File.ReadAllTextAsync(Path.Combine(c.Root, ".odysseum", "project.v1.json")) == legacy, "Legacy backup is not exact.");
        Require(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Chapter 01", "Arrival.md")) == prose, "Migration rewrote prose.");
        Require((await c.Project()).Revision == project.Revision, "Unchanged scans must not rewrite manifests.");
    }),
    ("Folder moves retain identity and file moves transfer metadata ownership", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript/First"), "Arrival", "Text");
        var before = await c.Project();
        await c.Documents.UpdateAsync(scene, Details("Arrival", "Keep me", "", DocumentStatus.Revised, 50), before.Revision);
        var oldFolder = Path.Combine(c.Root, "Manuscript", "First");
        var folderId = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(oldFolder, ".odysseum", "folder.json")))!["id"]!.GetValue<string>();
        Directory.Move(oldFolder, Path.Combine(c.Root, "Manuscript", "Renamed"));
        var renamed = await c.Documents.GetAsync(await c.Project(), scene.Id);
        Require(PathOf(renamed) == "Manuscript/Renamed/Arrival.md" && renamed.Synopsis == "Keep me", "Folder move lost metadata.");
        var parent = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", ".odysseum", "folder.json")))!;
        Require(parent["folders"]![folderId]!["path"]!.GetValue<string>() == "Renamed", "Parent did not track folder rename.");
        var moved = await c.Documents.MoveAsync(renamed, await Ensure(c, "Manuscript/Second"), null, renamed.Revision);
        var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Renamed", ".odysseum", "folder.json")))!;
        var target = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(c.Root, "Manuscript", "Second", ".odysseum", "folder.json")))!;
        Require(source["documents"]!.AsObject().All(pair => pair.Value!["path"]!.GetValue<string>().StartsWith('.')) && target["documents"]![scene.Id] is not null && moved.Synopsis == "Keep me", "Move left duplicate metadata owners.");
        Require(target["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).Contains(scene.Id) && !source["itemOrder"]!.AsArray().Select(x => x!.GetValue<string>()).Contains(scene.Id), "Move did not carry the document's place to the new folder.");
    }),
    ("External folder metadata changes invalidate revisions and preserve extensions", async c =>
    {
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript/First"), "Arrival", "Text");
        var before = await c.Project();
        var path = Path.Combine(c.Root, "Manuscript", "First", ".odysseum", "folder.json");
        var local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["custom"] = "folder extension";
        local["documents"]![scene.Id]!["synopsis"] = "From another editor";
        await File.WriteAllTextAsync(path, local.ToJsonString());
        await Expect(WorkspaceError.Conflict, () => c.Documents.UpdateAsync(scene, Details("Arrival", "Stale", "", DocumentStatus.Draft, 1), before.Revision));
        var current = await c.Project();
        Require(current.Documents.Where(Visible).Single().Synopsis == "From another editor" && current.Revision != before.Revision, "Folder edits were not observed.");
        await c.Documents.UpdateAsync(scene, Details("Arrival", "New", "", DocumentStatus.Done, 1), current.Revision);
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
        var original = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "Original");
        var path = Path.Combine(c.Root, PathOf(original));
        var bytes = await File.ReadAllBytesAsync(path);
        var replacementId = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(path, $"---\nwriter_id: {replacementId}\n---\n\nReplacement");
        Require((await c.Project()).Documents.Where(Visible).Single().Id == replacementId, "Replacement retained the wrong identity.");
        Require((await c.Project()).Documents.Where(Visible).Single().Id == replacementId, "Retained metadata made the next scan invalid.");
        await File.WriteAllBytesAsync(path, bytes);
        Require((await c.Documents.GetAsync(await c.Project(), original.Id)).Title == "Arrival", "Restoring the original lost its metadata.");
    }),
    ("Metadata directories from earlier releases are renamed when the project opens", async c =>
    {
        var legacyRoot = Path.Combine(c.Root, "Legacy");
        Directory.CreateDirectory(legacyRoot);
        await using (var first = new Workspace(c.Root))
        {
            var project = await first.ProjectAsync("Legacy");
            await first.Documents.CreateAsync(await EnsureFolder(first, project, "Manuscript"), "Arrival", "Keep prose");
            project = await first.ProjectAsync("Legacy");
            await first.Projects.SaveSettingsAsync(project, new ProjectSettings { Title = "Legacy title", WordGoal = 100 }, project.Revision);
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
        await permissive.Folders.CreateAsync(project.RootFolder, "Notes");
        project = await permissive.ProjectAsync("Permissive");
        await permissive.Folders.RemoveAsync(FolderAt(project, "Notes"), project.Revision);
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
            await first.Documents.CreateAsync(await EnsureFolder(first, project, "Manuscript"), "Arrival", "Keep prose");
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
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "Scene prose");
        var place = await c.Documents.CreateAsync(await Ensure(c, "Locations/Coast"), "Harbour", "Location research");
        var character = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "Biography");
        Require(place.Kind == DocumentKind.Location, "Nested location was classified as a scene.");
        var project = await c.Project();
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "", "", DocumentStatus.Draft, 1000, [character.Id, place.Id, place.Id]), project.Revision));
        Require(project.Document(scene.Id)!.LinkIds.SequenceEqual([character.Id, place.Id]), "Links were not stored or deduplicated.");
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(scene, Details("Rejected", "", "", DocumentStatus.Done, 1, [Guid.NewGuid().ToString()]), project.Revision));
        await Expect(WorkspaceError.Invalid, () => c.Documents.UpdateAsync(scene, Details("Rejected", "", "", DocumentStatus.Done, 1, Enumerable.Repeat(place.Id, 201).ToArray()), project.Revision));
        project = Snapshot(await c.Documents.UpdateAsync(scene, Details("Arrival", "Changed", "", DocumentStatus.Draft, 1000), project.Revision));
        Require(project.Document(scene.Id)!.LinkIds.SequenceEqual([character.Id, place.Id]), "Omitted links or rejected request cleared attachments.");
        Require(!(await c.Views.ExportAsync(project)).Contains("Location research"), "Locations leaked into manuscript export.");
        Require((await c.Views.SearchAsync(project, "Location research")).Single().Document.Id == place.Id, "Location prose is not searchable.");
    }),
    ("Versions keep the whole project, and a restore is itself a new version", async c =>
    {
        var open = await c.W.Library.OpenProjectAsync(c.Slug);
        var scene = await c.Documents.CreateAsync(await Ensure(c, "Manuscript"), "Arrival", "First draft");
        var external = Path.Combine(c.Root, "Notes", "crlf.md");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Line one\r\nLine two\r\n")];
        await File.WriteAllBytesAsync(external, bytes);
        var named = await open.SaveVersionAsync("  Draft one ");
        Require(named.Name == "Draft one" && !named.Automatic, "A named version should carry its trimmed name.");
        await c.Documents.SaveBodyAsync(scene, "Second draft", scene.Revision);
        await File.WriteAllTextAsync(external, "Rewritten\n");
        var extra = await c.Documents.CreateAsync(await Ensure(c, "Characters"), "Mara", "Biography");
        var automatic = await open.SaveAutomaticVersionAsync();
        Require(automatic is { Automatic: true, Name: null, Changes: >= 3 }, "An automatic version records every changed file.");
        Require(await open.SaveAutomaticVersionAsync() is null, "Nothing changed, so no version should be saved.");
        var versions = await open.ListVersionsAsync();
        Require(versions[0].Id == automatic!.Id && versions[1].Id == named.Id, "Versions list newest first.");
        Require((await File.ReadAllTextAsync(Path.Combine(c.Root, ".git", "info", "exclude"))).Contains("history"), "Recovery snapshots must stay out of versions.");
        var restored = await open.RestoreVersionAsync(named.Id);
        Require(restored.Document(extra.Id) is null && !File.Exists(Path.Combine(c.Root, PathOf(extra))), "A restore removes documents added since.");
        Require((await c.Documents.GetAsync(await c.Project(), scene.Id)).Body == "First draft", "A restore brings back the old prose.");
        Require((await File.ReadAllBytesAsync(external)).SequenceEqual(bytes), "Restored files must match byte for byte, BOM and CRLF included.");
        versions = await open.ListVersionsAsync();
        Require(versions[0].Name!.StartsWith("Restored") && versions[1].Id == automatic.Id, "A restore is a new version on top, not a rewind.");
        restored = await open.RestoreVersionAsync(versions[1].Id);
        Require(restored.Document(extra.Id) is not null && (await c.Documents.GetAsync(await c.Project(), scene.Id)).Body == "Second draft", "Restoring the version before a restore undoes it.");
        await Expect(WorkspaceError.NotFound, () => open.RestoreVersionAsync("0123456789abcdef0123456789abcdef01234567"));
        await Expect(WorkspaceError.NotFound, () => open.RestoreVersionAsync("HEAD~1"));
        await Expect(WorkspaceError.Invalid, () => open.SaveVersionAsync("  "));
    }),
};

var libraryChecks = new List<(string Name, Func<string, Workspace, Task> Run)>
{
    ("Creates project folders with unique names and their own metadata", async (root, w) =>
    {
        Require((await w.Library.ListAsync()).Count == 0, "An empty workspace should list no projects.");
        var first = await w.Library.CreateAsync(new("My Novel", 80000));
        var second = await w.Library.CreateAsync(new("My Novel", null));
        var odd = await w.Library.CreateAsync(new("Draft: Part 1", null));
        Require(first.Slug == "My Novel" && second.Slug == "My Novel-2" && odd.Slug == "Draft- Part 1", "Folder names were not derived safely.");
        Require(File.Exists(Path.Combine(root, "My Novel", ".odysseum", "project.json")), "The project folder has no metadata.");
        var view = await w.ProjectAsync("My Novel");
        Require(view.Title == "My Novel" && view.WordGoal == 80000, "Title or goal was not stored.");
        await w.Documents.CreateAsync(FolderAt(await w.ProjectAsync(odd.Slug), "Manuscript"), "Scene", "Text");
        Require((await w.Library.ListAsync()).Select(x => x.Title).SequenceEqual(["Draft: Part 1", "My Novel", "My Novel"]), "Listing should show manifest titles, even with documents present.");
    }),
    ("New projects seed default folders in sidebar order and protect them from removal", async (root, w) =>
    {
        var created = await w.Library.CreateAsync(new("Seeded", null));
        var project = await w.ProjectAsync(created.Slug);
        Require(Directory.Exists(Path.Combine(root, created.Slug, "Manuscript", "Chapter 01")) && project.FolderAt("Manuscript/Chapter 01") is not null, "Chapter 01 was not seeded.");
        Require((await w.Order.FoldersAsync(project.RootFolder)).Select(f => f.Name).SequenceEqual(ProjectLibrary.DefaultFolders), "Default folders were not ordered.");
        var scene = project.Documents.Single(Scene);
        Require(scene.Path == "Manuscript/Chapter 01/Scene 01.md" && scene.Title == "Scene 01" && scene.WordGoal == 1000, "Scene 01 was not seeded.");
        await Expect(WorkspaceError.Forbidden, () => w.Folders.RemoveAsync(FolderAt(project, "Threads"), project.Revision));
        await Expect(WorkspaceError.Conflict, () => w.Folders.RemoveAsync(FolderAt(project, "Manuscript/Chapter 01"), project.Revision));
    }),
    ("Project templates capture a project by path and seed new projects with fresh ids", async (root, _) =>
    {
        var templates = new TemplateRepository(Path.Combine(root, ".templates"));
        templates.EnsureDefault();
        Require(File.Exists(Path.Combine(root, ".templates", "Default.json")) && templates.List().Single().Name == "Default", "The Default template was not written.");
        await using var w = new Workspace(Path.Combine(root, "workspace"), templates: templates, watch: true);
        var sourceSlug = (await w.Library.CreateAsync(new("Source", 70000))).Slug;
        var project = await w.ProjectAsync(sourceSlug);
        await w.Folders.CreateAsync(FolderAt(project, "Manuscript"), "Chapter 02");
        project = await w.ProjectAsync(sourceSlug);
        var scene = await w.Documents.CreateAsync(FolderAt(project, "Manuscript/Chapter 02"), "Opening", "Once.");
        var mara = await w.Documents.CreateAsync(FolderAt(project, "Characters"), "Mara", "Sheet");
        project = await w.ProjectAsync(sourceSlug);
        project = Snapshot(await w.Documents.UpdateAsync(scene, Details("The Opening", "It begins.", "", DocumentStatus.Done, 250, [mara.Id],
            new() { [mara.Id] = "First sight" }), project.Revision));
        var characters = FolderAt(project, "Characters");
        var chapter2 = await w.Folders.SetLayoutAsync(FolderAt(project, "Manuscript/Chapter 02"), new FolderLayout { PinnedView = FolderView.Board, GridFolderId = characters.Id }, project.Revision);
        await w.Order.ArrangeChildrenAsync(chapter2, [scene.Id], chapter2.Project.Revision);

        var saved = templates.Save(w.Templates.Capture(await w.ProjectAsync(sourceSlug), "Novel"));
        Require(saved.Documents.Select(d => d.Path).SequenceEqual(["Manuscript/Chapter 01/Scene 01.md", "Manuscript/Chapter 02/Opening.md", "Characters/Mara.md", "Styles/Default.md"]),
            "The template should list documents in order and leave out folders' own documents.");
        var json = File.ReadAllText(Path.Combine(root, ".templates", "Novel.json"));
        Require(!json.Contains(scene.Id) && !json.Contains("Once.") && !json.Contains("It begins."), "A project template carries neither ids nor what documents hold.");

        var view = await w.ProjectAsync((await w.Library.CreateAsync(new("Copy", null, "Novel"))).Slug);
        var opening = view.Documents.Single(d => d.Path == "Manuscript/Chapter 02/Opening.md");
        var sheet = view.Documents.Single(d => d.Path == "Characters/Mara.md");
        Require(view.Title == "Copy" && view.WordGoal == 70000, "Template goals were not applied.");
        Require(opening.Id != scene.Id && sheet.Id != mara.Id, "Documents made from a template need their own ids.");
        Require(opening.Title == "The Opening" && opening.Synopsis == "" && opening.WordGoal == 1000 && opening.Status == DocumentStatus.Draft
            && opening.LinkIds.Count == 0 && opening.Body == "", "Documents made from a template should start empty under their title.");
        var chapter = FolderAt(view, "Manuscript/Chapter 02");
        Require(chapter.PinnedView == FolderView.Board && (await w.Order.ChildrenAsync(chapter)).Select(child => child.Document!.Id).SequenceEqual([opening.Id])
            && chapter.GridFolder?.Id == FolderAt(view, "Characters").Id && chapter.GridFolder?.Id != characters.Id, "Folder layouts were not rebuilt.");
        Require((await w.Order.FoldersAsync(view.RootFolder)).Select(f => f.Name).SequenceEqual(ProjectLibrary.DefaultFolders), "The root order was lost.");

        await Expect(WorkspaceError.NotFound, () => w.Library.CreateAsync(new("Orphan", null, "Missing")));
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
        Require((await w.Library.ListAsync()).Select(x => x.Slug).SequenceEqual(["Dropped in"]), "Only real project folders should be listed.");
        var view = await w.ProjectAsync("Dropped in");
        Require(view.Title == "Dropped in", "The folder name should become the working title.");
        Require(File.Exists(Path.Combine(root, "Dropped in", ".odysseum", "project.json")), "Opening should create metadata.");
    }),
    ("Rejects unsafe project names and reports missing projects", async (_, w) =>
    {
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync("../outside"));
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync(".odysseum"));
        await Expect(WorkspaceError.Invalid, () => w.Projects.GetAsync("nested/name"));
        await Expect(WorkspaceError.Invalid, () => w.Library.CreateAsync(new("   ", null)));
        await Expect(WorkspaceError.NotFound, () => w.Projects.GetAsync("Missing"));
    }),
    ("Keeps projects isolated and reuses one open project per folder", async (_, w) =>
    {
        await w.Library.CreateAsync(new("One", null));
        await w.Library.CreateAsync(new("Two", null));
        var one = await w.Library.OpenProjectAsync("One");
        Require(ReferenceEquals(one, await w.Library.OpenProjectAsync("One")), "A project should open once per process.");
        await w.Documents.CreateAsync(FolderAt(await w.ProjectAsync("One"), "Manuscript"), "Only here", "Text");
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
        var listed = (await w.Library.ListAsync()).Single();
        Require(listed.Title == "Legacy title" && listed.Id == id, "Listing did not understand legacy metadata.");
        Require(await File.ReadAllTextAsync(manifestPath) == legacy, "Listing rewrote project metadata.");
        await File.WriteAllTextAsync(manifestPath, "{ broken metadata");
        listed = (await w.Library.ListAsync()).Single();
        Require(listed.Title == "Legacy" && listed.Id == "", "Invalid metadata prevented listing the folder.");
        await Expect(WorkspaceError.Corrupt, () => w.Projects.GetAsync("Legacy"));
        await File.WriteAllTextAsync(manifestPath, legacy);
        var opened = await w.ProjectAsync("Legacy");
        Require(opened.Id == id, "A failed open leaked its instance lock or prevented retry.");
        Require((await w.Projects.GetAsync(id)).Id == id, "A project should also open by its UUID.");
    }),
};

await using (var workspace = new Workspace(testRoot))
{
    foreach (var (name, check) in checks)
    {
        var slug = passed.ToString();
        var root = Path.Combine(testRoot, slug);
        Directory.CreateDirectory(root);
        await check(new Check(root, slug, workspace));
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
static Project Snapshot(IDocument document) => (Project)document.Project;
static Folder FolderAt(IProject project, string path) => ((Project)project).FolderAt(path) ?? throw new Exception($"There is no folder at '{path}'.");
static Task<IFolder> EnsureFolder(Workspace workspace, IProject project, string path) => FolderPaths.EnsureAsync(workspace.Folders, project, path);
static async Task<IFolder> Ensure(Check check, string path) => await EnsureFolder(check.W, await check.Project(), path);
static async Task<int> Position(Check check, IProject project, string id) =>
    (await check.Order.DocumentsAsync(project)).Select((document, index) => (document, index)).Single(pair => pair.document.Id == id).index;
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
        Library = new ProjectLibrary(root, new ProjectFactory(NullLoggerFactory.Instance, 300, watch: watch), templates);
        Projects = new ProjectService(Library);
        Folders = new FolderService(Library, settings);
        Documents = new DocumentService(Library);
        Order = new OrderService();
        Views = new ProjectViews(Order);
        Templates = new ProjectTemplateService();
    }

    public ProjectLibrary Library { get; }
    public IProjectService Projects { get; }
    public IFolderService Folders { get; }
    public IDocumentService Documents { get; }
    public IOrderService Order { get; }
    public ProjectViews Views { get; }
    public ProjectTemplateService Templates { get; }

    public async Task<Project> ProjectAsync(string slug) => (Project)await Projects.GetAsync(slug);

    public ValueTask DisposeAsync() => Library.DisposeAsync();
}

sealed class Check(string root, string slug, Workspace workspace)
{
    public string Root => root;
    public string Slug => slug;
    public Workspace W => workspace;
    public IProjectService Projects => workspace.Projects;
    public IFolderService Folders => workspace.Folders;
    public IDocumentService Documents => workspace.Documents;
    public IOrderService Order => workspace.Order;
    public ProjectViews Views => workspace.Views;
    public Task<Project> Project() => workspace.ProjectAsync(slug);
}

sealed class AllowingSettings : ISettingsProvider
{
    public IServerSettings GetSettings(bool copy = false) => new ServerSettings { AllowDeletingDefaultFolders = true };
    public void SaveSettings(IServerSettings settings) { }
    public void SaveSettings() { }
    public void DebugSettingsToLog() { }
}
