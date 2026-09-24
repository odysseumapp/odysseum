using System.Text;
using System.Text.Json.Nodes;
using Odysseum.Server.API.Enums;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;
using Microsoft.Extensions.Logging.Abstractions;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<string, ProjectServices, Task> Run)>
{
    ("Folders retain layouts and identity through scans and external moves", async (root, store) =>
    {
        var project = await store.GetProjectAsync();
        project = await store.CreateFolderAsync(new("Topics", project.Revision));
        project = await store.CreateFolderAsync(new("Topics/Race", project.Revision));
        var doc = await store.CreateAsync(new("Revelation", "Topics/Race", "Text"));
        var thread = await store.CreateAsync(new("Race", "Threads", "Thread notes"));
        project = await store.GetProjectAsync();
        project = await store.SaveFolderLayoutAsync(new("Topics/Race", "grid", [doc.Document.Id], project.Folders.Single(f => f.Path == "Threads").Id, project.Revision));
        var folder = project.Folders.Single(f => f.Path == "Topics/Race");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Topics/Race/.odysseum/folder.json")))!;
        Require(manifest["pinnedView"]!.GetValue<string>() == "grid", "Pin was not stored in the owning folder.");
        Directory.Move(Path.Combine(root, "Topics/Race"), Path.Combine(root, "Topics/Class"));
        var moved = (await store.GetProjectAsync()).Folders.Single(f => f.Path == "Topics/Class");
        Require(moved.Id == folder.Id && moved.PinnedView == "grid" && moved.GridFolder == folder.GridFolder && folder.GridFolder is not null, "Folder layout or identity was lost on move.");
    }),
    ("Folder removal refuses content and archives empty folder metadata", async (root, store) =>
    {
        var project = await store.GetProjectAsync();
        project = await store.CreateFolderAsync(new("Empty", project.Revision));
        var id = project.Folders.Single(f => f.Path == "Empty").Id;
        await File.WriteAllTextAsync(Path.Combine(root, "Empty/.keep"), "Keep this hidden file");
        await Expect(409, () => store.RemoveFolderAsync(new("Empty", project.Revision)));
        File.Delete(Path.Combine(root, "Empty/.keep"));
        project = await store.RemoveFolderAsync(new("Empty", project.Revision));
        Require(project.Folders.All(f => f.Path != "Empty") && !Directory.Exists(Path.Combine(root, "Empty")), "Empty folder remains visible.");
        var archived = Directory.GetFiles(Path.Combine(root, ".odysseum/removed-folders"), "folder.json", SearchOption.AllDirectories);
        Require(archived.Length == 1 && (await File.ReadAllTextAsync(archived[0])).Contains(id), "Removed metadata was lost.");
        await Expect(400, () => store.RemoveFolderAsync(new("", project.Revision)));
        await Expect(400, () => store.CreateFolderAsync(new("../Outside", project.Revision)));
    }),
    ("Folder layouts validate immediate children and reject stale writes", async (_, store) =>
    {
        var first = await store.CreateAsync(new("First", "One", ""));
        var other = await store.CreateAsync(new("Other", "Two", ""));
        var project = await store.GetProjectAsync();
        await Expect(400, () => store.SaveFolderLayoutAsync(new("One", "board", [other.Document.Id], null, project.Revision)));
        await Expect(400, () => store.SaveFolderLayoutAsync(new("One", "board", [], Guid.NewGuid().ToString(), project.Revision)));
        await Expect(400, () => store.SaveFolderLayoutAsync(new("One", "board", [], first.Document.Id, project.Revision)));
        await Expect(400, () => store.SaveFolderLayoutAsync(new("One", "threads", [], null, project.Revision)));
        var two = project.Folders.Single(f => f.Path == "Two").Id;
        project = await store.SaveFolderLayoutAsync(new("", "outline", ["folder:Two", "folder:One"], two, project.Revision));
        var stale = project.Revision;
        project = await store.CreateFolderAsync(new("Three", project.Revision));
        await Expect(409, () => store.SaveFolderLayoutAsync(new("", "board", [], null, stale)));
        Require(project.Folders.Single(f => f.Path == "").GridFolder == two, "Grid column folder was lost.");
        Require((await store.GetProjectAsync()).Folders.Single(f => f.Path == "").ItemOrder.SequenceEqual(new[] { "folder:Two", "folder:One" }), "Root folder order was lost.");
        await Expect(409, () => store.RemoveFolderAsync(new("One", project.Revision)));
    }),
    ("Reads an existing folder without rewriting its Markdown", async (root, store) =>
    {
        var path = Path.Combine(root, "existing.md");
        var original = Encoding.UTF8.GetBytes("# Existing\n\nText with **meaning**.\n");
        await File.WriteAllBytesAsync(path, original);
        var project = await store.GetProjectAsync();
        Require(project.Documents.Count(Visible) == 1, "Document was not discovered.");
        var after = await File.ReadAllBytesAsync(path);
        Require(original.SequenceEqual(after), "Scan rewrote the file.");
    }),
    ("Preserves UTF-8 BOM, CRLF, frontmatter, and unsupported Markdown", async (root, store) =>
    {
        var id = Guid.NewGuid().ToString();
        var prefix = $"\uFEFF---\r\nwriter_id: {id}\r\ncustom: [keep, exactly]\r\n---\r\n\r\n";
        const string body = "Paragraph.\r\n\r\n[^note]: unknown extension\r\n<div data-test=\"yes\">HTML stays text</div>\r\n";
        var path = Path.Combine(root, "format.md");
        await File.WriteAllTextAsync(path, prefix + body, new UTF8Encoding(false));
        var doc = await store.GetDocumentAsync(id);
        Require(doc.Content == body, "Frontmatter was not split exactly.");
        await store.SaveAsync(id, new(body + "Next paragraph.\r\n", doc.Document.Revision));
        Require(await File.ReadAllTextAsync(path) == prefix.TrimStart('\uFEFF') + body + "Next paragraph.\r\n", "File formatting was changed.");
        Require((await File.ReadAllBytesAsync(path)).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "BOM was lost.");
    }),
    ("Rejects a stale browser save after an external edit with unchanged timestamps", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Conflict", "Manuscript", "Original text"));
        var path = Path.Combine(root, doc.Document.Path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var raw = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, raw.Replace("Original text", "External text"));
        File.SetLastWriteTimeUtc(path, timestamp);
        await Expect(409, () => store.SaveAsync(doc.Document.Id, new("Browser text", doc.Document.Revision)));
        Require((await File.ReadAllTextAsync(path)).Contains("External text"), "External edits were overwritten.");
    }),
    ("Serializes simultaneous browser saves so exactly one succeeds", async (_, store) =>
    {
        var doc = await store.CreateAsync(new("Two tabs", "", "Start"));
        async Task<bool> Save(string text)
        {
            try { await store.SaveAsync(doc.Document.Id, new(text, doc.Document.Revision)); return true; }
            catch (WorkspaceException ex) when (ex.Status == 409) { return false; }
        }
        var results = await Task.WhenAll(Save("First tab"), Save("Second tab"));
        Require(results.Count(x => x) == 1, "Expected one success and one conflict.");
    }),
    ("Serializes competing settings and document metadata updates", async (_, store) =>
    {
        var doc = await store.CreateAsync(new("Original", "Manuscript", "Text"));
        var before = await store.GetProjectAsync();
        async Task<bool> Attempt(Func<Task> change)
        {
            try { await change(); return true; }
            catch (WorkspaceException ex) when (ex.Status == 409) { return false; }
        }
        var results = await Task.WhenAll(
            Attempt(() => store.UpdateSettingsAsync(new ProjectSettings { Title = "Updated project", WordGoal = 12345 }, before.Revision)),
            Attempt(() => store.UpdateMetadataAsync(doc.Document.Id, new("Updated document", "", "", DocumentStatus.Revised, 500, before.Revision))));
        Require(results.Count(success => success) == 1, "Expected exactly one metadata revision to succeed.");
        var after = await store.GetProjectAsync();
        Require(after.Settings.Title == (results[0] ? "Updated project" : before.Settings.Title), "Settings did not match the successful update.");
        Require(after.Documents.Where(Visible).Single().Title == (results[1] ? "Updated document" : "Original"), "Rejected document metadata leaked into state.");
    }),
    ("Retains metadata through an external move and content edit", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("A scene", "Manuscript", "Before"));
        var project = await store.GetProjectAsync();
        await store.UpdateMetadataAsync(doc.Document.Id, new("A better title", "A synopsis", "Private notes", DocumentStatus.Revised, 900, project.Revision));
        var oldPath = Path.Combine(root, doc.Document.Path);
        var target = Path.Combine(root, "Other", "renamed.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(oldPath, target);
        await File.AppendAllTextAsync(target, "\nExternal addition.");
        var moved = await store.GetDocumentAsync(doc.Document.Id);
        Require(moved.Document.Path == "Other/renamed.md" && moved.Document.Synopsis == "A synopsis" && moved.Document.Title == "A better title", "Identity or metadata was lost.");
        Require(moved.Content.Contains("External addition"), "External edit was missed.");
    }),
    ("Does not recreate externally deleted documents", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Deleted", "", "Before"));
        var path = Path.Combine(root, doc.Document.Path);
        File.Delete(path);
        await Expect(404, () => store.SaveAsync(doc.Document.Id, new("Unsaved", doc.Document.Revision)));
        Require(!File.Exists(path), "A deleted file was recreated.");
    }),
    ("Preserves both revisions in readable snapshots", async (_, store) =>
    {
        var doc = await store.CreateAsync(new("History", "", "First words"));
        await store.SaveAsync(doc.Document.Id, new("Second words", doc.Document.Revision));
        var snapshots = await store.GetSnapshotsAsync(doc.Document.Id);
        Require(snapshots.Count == 2, "Both sides of the save must be recoverable.");
        var contents = new List<string>();
        foreach (var snapshot in snapshots) contents.Add(await store.GetSnapshotAsync(doc.Document.Id, snapshot.Id));
        Require(contents.Contains("First words") && contents.Contains("Second words"), "Missing revision content.");
    }),
    ("Checks metadata revisions and preserves unknown JSON fields", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Metadata", "", "Text"));
        var oldProject = await store.GetProjectAsync();
        var path = Path.Combine(root, ".odysseum", "project.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["customTool"] = "keep me";
        json["documents"]![doc.Document.Id]!["customField"] = 42;
        json["settings"]!["title"] = "External project name";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Expect(409, () => store.UpdateSettingsAsync(new ProjectSettings { Title = "Stale browser title", WordGoal = 100 }, oldProject.Revision));
        var current = await store.GetProjectAsync();
        await store.UpdateSettingsAsync(new ProjectSettings { Title = "Updated title", WordGoal = 200 }, current.Revision);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Require(saved["customTool"]!.GetValue<string>() == "keep me" && saved["documents"]![doc.Document.Id]!["customField"]!.GetValue<int>() == 42, "Unknown fields were discarded.");
    }),
    ("Refuses malformed metadata without overwriting it", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Protected", "", "Keep this"));
        var path = Path.Combine(root, ".odysseum", "project.json");
        await File.WriteAllTextAsync(path, "{ broken external edit");
        await Expect(422, () => store.SaveAsync(doc.Document.Id, new("New text", doc.Document.Revision)));
        Require(await File.ReadAllTextAsync(path) == "{ broken external edit", "Malformed metadata was replaced.");
    }),
    ("Blocks traversal and hidden internal paths", async (_, store) =>
    {
        await Expect(400, () => store.CreateAsync(new("Escape", "../outside", "No")));
        await Expect(400, () => store.CreateAsync(new("Escape", ".odysseum", "No")));
        await Expect(400, () => store.CreateAsync(new("Escape", "C:/outside", "No")));
        var doc = await store.CreateAsync(new("Safe", "", "Text"));
        await Expect(400, () => store.MoveAsync(doc.Document.Id, new("../escape.md", doc.Document.Revision)));
    }),
    ("Rejects copied document IDs without confusing their content", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Original", "", "Keep"));
        File.Copy(Path.Combine(root, doc.Document.Path), Path.Combine(root, "copied.md"));
        await Expect(409, () => store.GetProjectAsync());
        Require((await File.ReadAllTextAsync(Path.Combine(root, "copied.md"))).Contains("Keep"), "Copy was modified.");
    }),
    ("Exports manuscript order and excludes story notes", async (_, store) =>
    {
        var first = await store.CreateAsync(new("First", "Manuscript", "First body"));
        var second = await store.CreateAsync(new("Second", "Manuscript", "Second body"));
        var note = await store.CreateAsync(new("Secret notes", "Notes", "Research only"));
        var project = await store.GetProjectAsync();
        await store.ReorderAsync(new([second.Document.Id, first.Document.Id, note.Document.Id], project.Revision));
        var export = await store.ExportAsync();
        Require(export.IndexOf("Second body", StringComparison.Ordinal) < export.IndexOf("First body", StringComparison.Ordinal), "Export order is wrong.");
        Require(!export.Contains("Research only") && !export.Contains("writer_id"), "Export included internal metadata or notes.");
    }),
    ("Searches prose, synopsis, and author notes", async (_, store) =>
    {
        var doc = await store.CreateAsync(new("Find me", "", "The cartographer left at dawn."));
        var project = await store.GetProjectAsync();
        await store.UpdateMetadataAsync(doc.Document.Id, new("Find me", "A lighthouse", "Remember Bellwether", DocumentStatus.Draft, 500, project.Revision));
        foreach (var query in new[] { "CARTOGRAPHER", "lighthouse", "Bellwether" })
            Require((await store.SearchAsync(query)).Single().Document.Id == doc.Document.Id, "Search missed " + query);
    }),
    ("Migrates a flat legacy manifest and applies the default scene goal", async (root, store) =>
    {
        var path = Path.Combine(root, ".odysseum", "project.json");
        await File.WriteAllTextAsync(path, "{\"version\": 1, \"id\": \"" + Guid.NewGuid() + "\", \"title\": \"Legacy title\", \"wordGoal\": 12345, \"documents\": {}}");
        var project = await store.GetProjectAsync();
        Require(project.Settings.Title == "Legacy title" && project.Settings.WordGoal == 12345, "Legacy top-level settings were not migrated.");
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Require(written["settings"]!["title"]!.GetValue<string>() == "Legacy title" && written["title"] is null, "Manifest was not rewritten in the grouped form.");
        await Expect(400, () => store.UpdateSettingsAsync(new ProjectSettings { Title = " ", WordGoal = 1 }, project.Revision));
        var updated = await store.UpdateSettingsAsync(new ProjectSettings { Title = "Legacy title", WordGoal = 12345, DefaultSceneWordGoal = 700 }, project.Revision);
        var doc = await store.CreateAsync(new("Fresh scene", "Manuscript", "Text"));
        Require(doc.Document.WordGoal == 700 && updated.Settings.DefaultSceneWordGoal == 700, "New scenes should take the project's default goal.");
    }),
    ("Classifies documents by folder and attaches characters to scenes", async (_, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Text"));
        var character = await store.CreateAsync(new("Mara", "Characters", "A cartographer."));
        var note = await store.CreateAsync(new("Island", "Notes", "Research"));
        Require(scene.Document.Kind == DocumentKind.Scene && character.Document.Kind == DocumentKind.Character && note.Document.Kind == DocumentKind.Note, "Kinds were not derived from folders.");
        var project = await store.GetProjectAsync();
        var updated = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, [character.Document.Id]));
        Require(updated.Documents.Single(x => x.Id == scene.Document.Id).Links.SequenceEqual([character.Document.Id]), "Attached characters were not stored.");
        Require(updated.Documents.Single(x => x.Id == character.Document.Id).Links.SequenceEqual([scene.Document.Id]), "Links are undirected: the character should list the scene.");
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, updated.Revision, [scene.Document.Id])));
        Require(!(await store.ExportAsync()).Contains("A cartographer."), "Characters must not appear in the manuscript export.");
    }),
    ("An invalid metadata request leaves existing details unchanged", async (_, store) =>
    {
        var doc = await store.CreateAsync(new("Keep title", "", "Text"));
        var project = await store.GetProjectAsync();
        await Expect(400, () => store.UpdateMetadataAsync(doc.Document.Id, new("Wrong title", "", "", (DocumentStatus)99, 5, project.Revision)));
        Require((await store.GetDocumentAsync(doc.Document.Id)).Document.Title == "Keep title", "Rejected request partially changed metadata.");
    }),
    ("Rejected character attachments never persist other metadata changes", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Keep title", "Manuscript", "Text"));
        var character = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id,
            new("Keep title", "Keep synopsis", "Keep notes", DocumentStatus.Draft, 1000, project.Revision, [character.Document.Id]));
        var manifestPath = Path.Combine(root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        foreach (var attachments in new[] { new[] { scene.Document.Id }, Enumerable.Repeat(character.Document.Id, 201).ToArray() })
        {
            await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id,
                new("Rejected title", "Rejected synopsis", "Rejected notes", DocumentStatus.Revised, 5, project.Revision, attachments)));
            await store.ScanAsync();
            var current = await store.GetProjectAsync();
            var details = current.Documents.Single(x => x.Id == scene.Document.Id);
            Require(details.Title == "Keep title" && details.Synopsis == "Keep synopsis" && details.Notes == "Keep notes"
                && details.Status == DocumentStatus.Draft && details.WordGoal == 1000
                && details.Links.SequenceEqual([character.Document.Id]), "Rejected request changed the in-memory metadata.");
            var persisted = await File.ReadAllBytesAsync(manifestPath);
            Require(current.Revision == project.Revision && original.SequenceEqual(persisted),
                "A later scan persisted a rejected request.");
        }
    }),
    ("Failed scans do not publish partially discovered metadata", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Original", "Manuscript", "Keep this"));
        var before = await store.GetProjectAsync();
        var candidate = Path.Combine(root, "A new file.md");
        var duplicate = Path.Combine(root, "Z duplicate.md");
        await File.WriteAllTextAsync(candidate, "Discovered before the duplicate ID.");
        File.Copy(Path.Combine(root, scene.Document.Path), duplicate);
        await Expect(409, () => store.ScanAsync());
        File.Delete(candidate);
        File.Delete(duplicate);
        var after = await store.GetProjectAsync();
        Require(after.Revision == before.Revision && after.Documents.Count(Visible) == 1,
            "An aborted scan left partial changes in the manifest.");
    }),
    ("Failed manifest replacements leave metadata, settings, and order unchanged", async (root, store) =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = await store.CreateAsync(new("First", "Manuscript", "Text"));
        await store.CreateAsync(new("Second", "Manuscript", "Text"));
        var before = await store.GetProjectAsync();
        var manifestPath = Path.Combine(root, ".odysseum", "project.json");
        var original = await File.ReadAllBytesAsync(manifestPath);
        Func<Task>[] changes =
        [
            () => store.UpdateMetadataAsync(first.Document.Id, new("Rejected", "Synopsis", "Notes", DocumentStatus.Revised, 5, before.Revision)),
            () => store.UpdateSettingsAsync(new ProjectSettings { Title = "Rejected settings", WordGoal = 5 }, before.Revision),
            () => store.ReorderAsync(new(before.Documents.Select(x => x.Id).Reverse().ToArray(), before.Revision)),
        ];
        foreach (var change in changes)
        {
            var failed = false;
            using (var locked = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var folderLocked = new FileStream(Path.Combine(root, "Manuscript", ".odysseum", "folder.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { await change(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            }
            Require(failed, "Expected the locked manifest to reject replacement.");
            var after = await store.GetProjectAsync();
            var persisted = await File.ReadAllBytesAsync(manifestPath);
            Require(after.Revision == before.Revision && original.SequenceEqual(persisted),
                "A later scan persisted changes from a failed replacement.");
            Require(!Directory.EnumerateFiles(Path.GetDirectoryName(manifestPath)!, ".odysseum-*.tmp").Any(),
                "A failed replacement left a temporary file behind.");
        }
    }),
    ("Null document metadata is rejected without changing the manifest", async (root, store) =>
    {
        var doc = await store.CreateAsync(new("Keep", "Manuscript", "Text"));
        var manifestPath = Path.Combine(root, "Manuscript", ".odysseum", "folder.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        manifest["documents"]![doc.Document.Id] = null;
        var invalid = manifest.ToJsonString();
        await File.WriteAllTextAsync(manifestPath, invalid);
        await Expect(422, () => store.GetProjectAsync());
        Require(await File.ReadAllTextAsync(manifestPath) == invalid, "Invalid metadata was rewritten.");
    }),
};

checks.AddRange([
    ("A link carries one note shared by both ends, and the note goes when the link does", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Scene prose"));
        var thread = await store.CreateAsync(new("Race", "Threads", "Thread notes"));
        var other = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, [thread.Document.Id],
            new() { [thread.Document.Id] = "  Mara first doubts the map  ", [other.Document.Id] = "Not linked" }));
        var notes = project.Documents.Single(d => d.Id == scene.Document.Id).LinkNotes;
        Require(notes.Count == 1 && notes[thread.Document.Id] == "Mara first doubts the map", "The note was not stored trimmed, or a note without a link was kept.");
        Require(project.Documents.Single(d => d.Id == thread.Document.Id).LinkNotes[scene.Document.Id] == "Mara first doubts the map", "The other end should read the same note.");
        var threadManifest = Path.Combine(root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![thread.Document.Id]!["linkNotes"]![scene.Document.Id]!.GetValue<string>() == "Mara first doubts the map", "The note was not persisted on the other side.");
        project = await store.UpdateMetadataAsync(thread.Document.Id, new("Race", "", "", DocumentStatus.Draft, 1000, project.Revision, null, new() { [scene.Document.Id] = "Rewritten" }));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Synopsis", "", DocumentStatus.Draft, 1000, project.Revision));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).LinkNotes[thread.Document.Id] == "Rewritten", "A note edited from the other end did not change here, or an unrelated save lost it.");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!;
        manifest["documents"]![thread.Document.Id]!["linkNotes"] = new JsonObject();
        await File.WriteAllTextAsync(threadManifest, manifest.ToJsonString());
        project = await store.GetProjectAsync();
        Require(project.Documents.Single(d => d.Id == thread.Document.Id).LinkNotes[scene.Document.Id] == "Rewritten", "A one-sided note should read from the other end.");
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, null, new() { [thread.Document.Id] = new string('x', 2001) })));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, null, new()));
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "An empty note set should clear the note on both sides.");
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, null, new() { [thread.Document.Id] = "Back" }));
        project = await store.UpdateMetadataAsync(thread.Document.Id, new("Race", "", "", DocumentStatus.Draft, 1000, project.Revision, []));
        project = await store.UpdateMetadataAsync(thread.Document.Id, new("Race", "", "", DocumentStatus.Draft, 1000, project.Revision, [scene.Document.Id]));
        Require(project.Documents.All(d => d.LinkNotes.Count == 0), "Unlinking should take the note with it on both sides.");
    }),
    ("Links are undirected, kept on both sides, and legacy character, location and thread lists migrate", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Scene prose"));
        var first = await store.CreateAsync(new("Race", "Threads", "Thread notes only"));
        var second = await store.CreateAsync(new("Meet Cute", "Threads/Story Beats", "A nested thread"));
        var mara = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        Require(first.Document.Kind == DocumentKind.Thread && second.Document.Kind == DocumentKind.Thread, "Thread file was classified as a scene.");
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, [first.Document.Id, second.Document.Id]));
        var current = project.Documents.Single(d => d.Id == scene.Document.Id);
        Require(current.Links.SequenceEqual(new[] { first.Document.Id, second.Document.Id }) && current.Order == scene.Document.Order, "Linking changed manuscript order or lost a link.");
        Require(project.Documents.Single(d => d.Id == first.Document.Id).Links.SequenceEqual([scene.Document.Id]), "The thread should list the scene back.");
        var threadManifest = Path.Combine(root, "Threads", ".odysseum", "folder.json");
        Require(JsonNode.Parse(await File.ReadAllTextAsync(threadManifest))!["documents"]![first.Document.Id]!["links"]![0]!.GetValue<string>() == scene.Document.Id, "The reverse side was not persisted.");
        project = await store.UpdateMetadataAsync(second.Document.Id, new("Meet Cute", "", "", DocumentStatus.Draft, 1000, project.Revision, [scene.Document.Id, first.Document.Id]));
        Require(project.Documents.Single(d => d.Id == first.Document.Id).Links.OrderBy(x => x).SequenceEqual(new[] { scene.Document.Id, second.Document.Id }.OrderBy(x => x)), "Thread-to-thread link was not mirrored.");
        project = await store.UpdateMetadataAsync(first.Document.Id, new("Race", "", "", DocumentStatus.Draft, 1000, project.Revision, []));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).Links.SequenceEqual([second.Document.Id])
            && project.Documents.Single(d => d.Id == second.Document.Id).Links.SequenceEqual([scene.Document.Id]), "Removing a link from one side left it on the other.");
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Updated", "", DocumentStatus.Draft, 1000, project.Revision));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).Links.Count == 1, "Omitting links removed them.");
        var manuscript = Path.Combine(root, "Manuscript", ".odysseum", "folder.json");
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(manuscript))!;
        legacy["documents"]![scene.Document.Id]!["characters"] = new JsonArray(mara.Document.Id);
        legacy["documents"]![scene.Document.Id]!["threads"] = new JsonArray(first.Document.Id);
        legacy["threads"] = new JsonArray(first.Document.Id);
        legacy["threadAxis"] = "columns";
        legacy["pinnedView"] = "threads";
        await File.WriteAllTextAsync(manuscript, legacy.ToJsonString());
        project = await store.GetProjectAsync();
        current = project.Documents.Single(d => d.Id == scene.Document.Id);
        Require(current.Links.OrderBy(x => x).SequenceEqual(new[] { second.Document.Id, mara.Document.Id, first.Document.Id }.OrderBy(x => x)), "Legacy character and thread lists were not read as links.");
        Require(project.Documents.Single(d => d.Id == mara.Document.Id).Links.SequenceEqual([scene.Document.Id]), "A one-sided legacy link should read back from the other side.");
        var folder = project.Folders.Single(f => f.Path == "Manuscript");
        Require(folder.PinnedView == "grid", "Legacy thread pin did not become the grid.");
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Updated again", "", DocumentStatus.Draft, 1000, project.Revision));
        var written = JsonNode.Parse(await File.ReadAllTextAsync(manuscript))!;
        Require(written["documents"]![scene.Document.Id]!["characters"] is null && written["documents"]![scene.Document.Id]!["links"]!.AsArray().Count == 3 && written["threads"] is null && written["threadAxis"] is null,
            "Legacy keys were not folded into links or dropped on write.");
        Require(!(await store.ExportAsync()).Contains("Thread notes only") && (await store.ExportAsync()).Contains("Scene prose"), "Thread notes were included in manuscript export.");
        var characters = project.Folders.Single(f => f.Path == "Characters").Id;
        project = await store.SaveFolderLayoutAsync(new("Manuscript", null, [], characters, project.Revision));
        Require(project.Folders.Single(f => f.Path == "Manuscript").GridFolder == characters, "Grid column folder was not saved.");
        project = await store.SaveFolderLayoutAsync(new("Manuscript", null, [], null, project.Revision));
        Require(project.Folders.Single(f => f.Path == "Manuscript").GridFolder is null, "Grid column folder was not cleared.");
    }),
    ("Every folder gets a hidden document named after it, hidden from listings that expect emptiness and export", async (root, store) =>
    {
        var project = await store.GetProjectAsync();
        project = await store.CreateFolderAsync(new("Manuscript", project.Revision));
        project = await store.CreateFolderAsync(new("Manuscript/Chapter 09", project.Revision));
        var chapter = project.Documents.Single(d => d.Path == "Manuscript/Chapter 09/.Chapter 09.md");
        Require(chapter.Title == "Chapter 09" && chapter.WordGoal == 0 && chapter.Kind == DocumentKind.Scene, "Folder document was not created with the folder's name.");
        Require(File.Exists(Path.Combine(root, "Manuscript", "Chapter 09", ".Chapter 09.md")), "Folder document is missing on disk.");
        Require(project.Documents.Any(d => d.Path == "Manuscript/.Manuscript.md") && project.Documents.All(d => d.Path != ".Sample manuscript.md" && !d.Path.StartsWith('.')), "Top-level folders get documents; the project root does not.");
        Directory.CreateDirectory(Path.Combine(root, "Manuscript", "Chapter 10"));
        await File.WriteAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 10", ".notes.md"), "ignored");
        project = await store.GetProjectAsync();
        Require(project.Documents.Any(d => d.Path == "Manuscript/Chapter 10/.Chapter 10.md") && project.Documents.All(d => !d.Path.EndsWith("/.notes.md")), "Dropped-in folder did not get its document, or another hidden file leaked in.");
        var scene = await store.CreateAsync(new("Arrival", "Manuscript/Chapter 09", "Scene prose"));
        project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(chapter.Id, new("Chapter 09", "Where it starts", "", DocumentStatus.Draft, 0, project.Revision, [scene.Document.Id]));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).Links.SequenceEqual([chapter.Id]), "Folder documents link like any other.");
        var saved = await store.SaveAsync(chapter.Id, new("# Nine\n\nAn epigraph.", (await store.GetDocumentAsync(chapter.Id)).Document.Revision));
        Require(saved.Content == "# Nine\n\nAn epigraph." && !(await store.ExportAsync()).Contains("An epigraph."), "Folder document prose was not saved, or leaked into the export.");
        project = await store.GetProjectAsync();
        project = await store.CreateFolderAsync(new("Manuscript/Empty", project.Revision));
        project = await store.RemoveFolderAsync(new("Manuscript/Empty", project.Revision));
        Require(project.Folders.All(f => f.Path != "Manuscript/Empty") && project.Documents.All(d => d.Path != "Manuscript/Empty/.Empty.md"), "A folder with only its own document could not be removed.");
        var plain = await store.CreateAsync(new(".Chapter 09", "Manuscript/Chapter 09", ""));
        Require(plain.Document.Path == "Manuscript/Chapter 09/Chapter 09.md", "A title starting with a dot must not create a hidden file.");
    }),
    ("Migrates nested metadata into immediate-child manifests without rewriting prose", async (root, store) =>
    {
        var sceneId = Guid.NewGuid().ToString();
        var characterId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(root, "Manuscript", "Chapter 01"));
        Directory.CreateDirectory(Path.Combine(root, "Characters"));
        Directory.CreateDirectory(Path.Combine(root, "Notes", "Empty"));
        var prose = $"---\nwriter_id: {sceneId}\n---\n\nUntouched prose.\n";
        await File.WriteAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 01", "Arrival.md"), prose);
        await File.WriteAllTextAsync(Path.Combine(root, "Characters", "Mara.md"), $"---\nwriter_id: {characterId}\n---\n\nBiography");
        var legacy = new JsonObject
        {
            ["version"] = 1, ["id"] = Guid.NewGuid().ToString(), ["title"] = "Legacy novel", ["custom"] = "retain root",
            ["documents"] = new JsonObject
            {
                [sceneId] = new JsonObject { ["path"] = "Manuscript/Chapter 01/Arrival.md", ["title"] = "A different title", ["synopsis"] = "Keep synopsis", ["order"] = 7, ["characters"] = new JsonArray(characterId), ["custom"] = "retain document" },
                [characterId] = new JsonObject { ["path"] = "Characters/Mara.md", ["title"] = "Mara", ["order"] = 9 }
            }
        }.ToJsonString();
        await File.WriteAllTextAsync(Path.Combine(root, ".odysseum", "project.json"), legacy);
        var project = await store.GetProjectAsync();
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".odysseum", "project.json")))!;
        Require(manifest["version"]!.GetValue<int>() == 2 && manifest["documents"]!.AsObject().Count == 0, "Root still owns nested documents.");
        Require(manifest["folders"]!.AsObject().Count == 3 && manifest["custom"]!.GetValue<string>() == "retain root", "Root folders or extension properties were lost.");
        var chapter = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 01", ".odysseum", "folder.json")))!;
        Require(chapter["documents"]![sceneId]!["path"]!.GetValue<string>() == "Arrival.md", "Chapter paths must be local filenames.");
        Require(chapter["documents"]![sceneId]!["custom"]!.GetValue<string>() == "retain document", "Document extension property was lost.");
        Require(File.Exists(Path.Combine(root, "Notes", "Empty", ".odysseum", "folder.json")), "Empty folders need manifests too.");
        var scene = project.Documents.Single(d => d.Id == sceneId);
        Require(scene.Title == "A different title" && scene.Synopsis == "Keep synopsis" && scene.Order == 7 && scene.Links.SequenceEqual([characterId]), "Migration lost document metadata or links.");
        Require(project.Documents.Single(d => d.Id == characterId).Links.SequenceEqual([sceneId]), "A legacy one-sided link should read as undirected.");
        Require(await File.ReadAllTextAsync(Path.Combine(root, ".odysseum", "project.v1.json")) == legacy, "Legacy backup is not exact.");
        Require(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 01", "Arrival.md")) == prose, "Migration rewrote prose.");
        Require((await store.GetProjectAsync()).Revision == project.Revision, "Unchanged scans must not rewrite manifests.");
    }),
    ("Folder moves retain identity and file moves transfer metadata ownership", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript/First", "Text"));
        var before = await store.GetProjectAsync();
        await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Keep me", "", DocumentStatus.Revised, 50, before.Revision));
        var oldFolder = Path.Combine(root, "Manuscript", "First");
        var folderId = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(oldFolder, ".odysseum", "folder.json")))!["id"]!.GetValue<string>();
        Directory.Move(oldFolder, Path.Combine(root, "Manuscript", "Renamed"));
        var renamed = await store.GetDocumentAsync(scene.Document.Id);
        Require(renamed.Document.Path == "Manuscript/Renamed/Arrival.md" && renamed.Document.Synopsis == "Keep me", "Folder move lost metadata.");
        var parent = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", ".odysseum", "folder.json")))!;
        Require(parent["folders"]![folderId]!["path"]!.GetValue<string>() == "Renamed", "Parent did not track folder rename.");
        var moved = await store.MoveAsync(scene.Document.Id, new("Manuscript/Second/Arrival.md", renamed.Document.Revision));
        var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Renamed", ".odysseum", "folder.json")))!;
        var target = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Second", ".odysseum", "folder.json")))!;
        Require(source["documents"]!.AsObject().All(pair => pair.Value!["path"]!.GetValue<string>().StartsWith('.')) && target["documents"]![scene.Document.Id] is not null && moved.Document.Synopsis == "Keep me", "Move left duplicate metadata owners.");
    }),
    ("External folder metadata changes invalidate revisions and preserve extensions", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript/First", "Text"));
        var before = await store.GetProjectAsync();
        var path = Path.Combine(root, "Manuscript", "First", ".odysseum", "folder.json");
        var local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["custom"] = "folder extension";
        local["documents"]![scene.Document.Id]!["synopsis"] = "From another editor";
        await File.WriteAllTextAsync(path, local.ToJsonString());
        await Expect(409, () => store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Stale", "", DocumentStatus.Draft, 1, before.Revision)));
        var current = await store.GetProjectAsync();
        Require(current.Documents.Where(Visible).Single().Synopsis == "From another editor" && current.Revision != before.Revision, "Folder edits were not observed.");
        await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "New", "", DocumentStatus.Done, 1, current.Revision));
        Require(JsonNode.Parse(await File.ReadAllTextAsync(path))!["custom"]!.GetValue<string>() == "folder extension", "Folder extension was lost.");
        local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["documents"]![scene.Document.Id]!["path"] = "../escape.md";
        var invalid = local.ToJsonString();
        await File.WriteAllTextAsync(path, invalid);
        await Expect(422, () => store.GetProjectAsync());
        Require(await File.ReadAllTextAsync(path) == invalid, "Unsafe folder manifest was rewritten.");
    }),
    ("Replacing a filename with a new ID does not invalidate retained metadata", async (root, store) =>
    {
        var original = await store.CreateAsync(new("Arrival", "Manuscript", "Original"));
        var path = Path.Combine(root, original.Document.Path);
        var bytes = await File.ReadAllBytesAsync(path);
        var replacementId = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(path, $"---\nwriter_id: {replacementId}\n---\n\nReplacement");
        Require((await store.GetProjectAsync()).Documents.Where(Visible).Single().Id == replacementId, "Replacement retained the wrong identity.");
        Require((await store.GetProjectAsync()).Documents.Where(Visible).Single().Id == replacementId, "Retained metadata made the next scan invalid.");
        await File.WriteAllBytesAsync(path, bytes);
        Require((await store.GetDocumentAsync(original.Document.Id)).Document.Title == "Arrival", "Restoring the original lost its metadata.");
    }),
    ("Metadata directories from earlier releases are renamed when the project opens", async (root, _) =>
    {
        var legacyRoot = Path.Combine(root, "Legacy");
        using (var first = new ProjectServices(legacyRoot, new ProjectEvents()))
        {
            await first.InitializeAsync();
            await first.CreateAsync(new("Arrival", "Manuscript", "Keep prose"));
            await first.UpdateSettingsAsync(new ProjectSettings { Title = "Legacy title", WordGoal = 100 }, (await first.GetProjectAsync()).Revision);
        }
        foreach (var directory in new[] { legacyRoot, Path.Combine(legacyRoot, "Manuscript") })
            Directory.Move(Path.Combine(directory, ".odysseum"), Path.Combine(directory, ".writer"));
        using var reopened = new ProjectServices(legacyRoot, new ProjectEvents());
        await reopened.InitializeAsync();
        var current = await reopened.GetProjectAsync();
        Require(current.Settings.Title == "Legacy title" && current.Documents.Where(Visible).Single().Title == "Arrival", "Legacy metadata was not carried over.");
        Require(Directory.Exists(Path.Combine(legacyRoot, "Manuscript", ".odysseum")) && !Directory.Exists(Path.Combine(legacyRoot, ".writer")), "Legacy metadata directories were not renamed.");
    }),
    ("Default folders can be removed when the server setting allows it", async (root, _) =>
    {
        using var store = new ProjectServices(Path.Combine(root, "Permissive"), new ProjectEvents(), new AllowingSettings());
        await store.InitializeAsync();
        var project = await store.GetProjectAsync();
        project = await store.CreateFolderAsync(new("Notes", project.Revision));
        project = await store.RemoveFolderAsync(new("Notes", project.Revision));
        Require(project.Folders.All(f => f.Path != "Notes"), "The default folder should be removable when allowed.");
    }),
    ("An interrupted manifest batch rolls back on reopening", async (root, _) =>
    {
        var recoveryRoot = Path.Combine(root, "Recovery");
        byte[] original;
        string folderPath;
        using (var first = new ProjectServices(recoveryRoot, new ProjectEvents()))
        {
            await first.InitializeAsync();
            await first.CreateAsync(new("Arrival", "Manuscript", "Keep prose"));
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
        using var reopened = new ProjectServices(recoveryRoot, new ProjectEvents());
        await reopened.InitializeAsync();
        Require((await reopened.GetProjectAsync()).Documents.Where(Visible).Single().Title == "Arrival", "Incomplete batch was treated as committed.");
        var restored = await File.ReadAllBytesAsync(folderPath);
        Require(original.SequenceEqual(restored), "Recovery did not restore the exact original manifest.");
        Require(!File.Exists(Path.Combine(recoveryRoot, ".odysseum", "pending-manifests.json")), "Recovery journal was not cleared.");
    }),
    ("Locations behave like characters and validate scene links", async (_, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Scene prose"));
        var place = await store.CreateAsync(new("Harbour", "Locations/Coast", "Location research"));
        var character = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        Require(place.Document.Kind == DocumentKind.Location, "Nested location was classified as a scene.");
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, [character.Document.Id, place.Document.Id, place.Document.Id]));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).Links.SequenceEqual([character.Document.Id, place.Document.Id]), "Links were not stored or deduplicated.");
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Done, 1, project.Revision, [Guid.NewGuid().ToString()])));
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Done, 1, project.Revision, Enumerable.Repeat(place.Document.Id, 201).ToArray())));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Changed", "", DocumentStatus.Draft, 1000, project.Revision));
        var details = project.Documents.Single(d => d.Id == scene.Document.Id);
        Require(details.Links.SequenceEqual([character.Document.Id, place.Document.Id]), "Omitted links or rejected request cleared attachments.");
        Require(!(await store.ExportAsync()).Contains("Location research"), "Locations leaked into manuscript export.");
        Require((await store.SearchAsync("Location research")).Single().Document.Id == place.Document.Id, "Location prose is not searchable.");
    }),
]);

var libraryChecks = new List<(string Name, Func<string, ProjectLibrary, Task> Run)>
{
    ("Creates project folders with unique names and their own metadata", async (root, library) =>
    {
        Require((await library.ListAsync()).Count == 0, "An empty workspace should list no projects.");
        var first = await library.CreateAsync(new("My Novel", 80000));
        var second = await library.CreateAsync(new("My Novel", null));
        var odd = await library.CreateAsync(new("Draft: Part 1", null));
        Require(first.Slug == "My Novel" && second.Slug == "My Novel-2" && odd.Slug == "Draft- Part 1", "Folder names were not derived safely.");
        Require(File.Exists(Path.Combine(root, "My Novel", ".odysseum", "project.json")), "The project folder has no metadata.");
        var view = await (await library.OpenAsync("My Novel")).Services.GetProjectAsync();
        Require(view.Settings.Title == "My Novel" && view.Settings.WordGoal == 80000, "Title or goal was not stored.");
        await (await library.OpenAsync(odd.Slug)).Services.CreateAsync(new("Scene", "Manuscript", "Text"));
        Require((await library.ListAsync()).Select(x => x.Title).SequenceEqual(["Draft: Part 1", "My Novel", "My Novel"]), "Listing should show manifest titles, even with documents present.");
    }),
    ("New projects seed default folders in sidebar order and protect them from removal", async (root, library) =>
    {
        var created = await library.CreateAsync(new("Seeded", null));
        var services = await library.OpenServicesAsync(created.Slug);
        var project = await services.GetProjectAsync();
        Require(Directory.Exists(Path.Combine(root, created.Slug, "Manuscript", "Chapter 01")) && project.Folders.Any(f => f.Path == "Manuscript/Chapter 01"), "Chapter 01 was not seeded.");
        Require(project.Folders.Single(f => f.Path == "").ItemOrder.SequenceEqual(ProjectLibrary.DefaultFolders.Select(name => "folder:" + name)), "Default folders were not ordered.");
        var scene = project.Documents.Single(Visible);
        Require(scene.Path == "Manuscript/Chapter 01/Scene 01.md" && scene.Title == "Scene 01" && scene.WordGoal == 1000, "Scene 01 was not seeded.");
        await Expect(403, () => services.RemoveFolderAsync(new("Threads", project.Revision)));
        await Expect(409, () => services.RemoveFolderAsync(new("Manuscript/Chapter 01", project.Revision)));
    }),
    ("Project templates capture a project by path and seed new projects with fresh ids", async (root, _) =>
    {
        var templates = new TemplateStore(Path.Combine(root, ".templates"));
        templates.EnsureDefault();
        Require(File.Exists(Path.Combine(root, ".templates", "Default.json")) && templates.List().Single().Name == "Default", "The Default template was not written.");
        await using var library = new ProjectLibrary(Path.Combine(root, "workspace"), new ProjectFactory(NullLoggerFactory.Instance, 300), templates);
        var source = await library.OpenServicesAsync((await library.CreateAsync(new("Source", 70000))).Slug);
        var project = await source.GetProjectAsync();
        project = await source.CreateFolderAsync(new("Manuscript/Chapter 02", project.Revision));
        var scene = await source.CreateAsync(new("Opening", "Manuscript/Chapter 02", "Once."));
        var mara = await source.CreateAsync(new("Mara", "Characters", "Sheet"));
        project = await source.GetProjectAsync();
        project = await source.UpdateMetadataAsync(scene.Document.Id, new("The Opening", "It begins.", "", DocumentStatus.Done, 250, project.Revision, [mara.Document.Id],
            new() { [mara.Document.Id] = "First sight" }));
        var characters = project.Folders.Single(f => f.Path == "Characters");
        await source.SaveFolderLayoutAsync(new("Manuscript/Chapter 02", "board", [scene.Document.Id], characters.Id, project.Revision));

        var saved = templates.Save(await source.CaptureTemplateAsync("Novel"));
        Require(saved.Documents.Select(d => d.Path).SequenceEqual(["Manuscript/Chapter 01/Scene 01.md", "Manuscript/Chapter 02/Opening.md", "Characters/Mara.md"]),
            "The template should list documents in order and leave out folders' own documents.");
        var json = File.ReadAllText(Path.Combine(root, ".templates", "Novel.json"));
        Require(!json.Contains(scene.Document.Id) && !json.Contains("Once.") && !json.Contains("It begins."), "A project template carries neither ids nor what documents hold.");

        var copy = await library.OpenServicesAsync((await library.CreateAsync(new("Copy", null, "Novel"))).Slug);
        var view = await copy.GetProjectAsync();
        var opening = view.Documents.Single(d => d.Path == "Manuscript/Chapter 02/Opening.md");
        var sheet = view.Documents.Single(d => d.Path == "Characters/Mara.md");
        Require(view.Settings.Title == "Copy" && view.Settings.WordGoal == 70000, "Template goals were not applied.");
        Require(opening.Id != scene.Document.Id && sheet.Id != mara.Document.Id, "Documents made from a template need their own ids.");
        Require(opening.Title == "The Opening" && opening.Synopsis == "" && opening.WordGoal == 1000 && opening.Status == DocumentStatus.Draft
            && opening.Links.Count == 0 && (await copy.GetDocumentAsync(opening.Id)).Content == "", "Documents made from a template should start empty under their title.");
        var chapter = view.Folders.Single(f => f.Path == "Manuscript/Chapter 02");
        Require(chapter.PinnedView == "board" && chapter.ItemOrder.SequenceEqual([opening.Id]) && chapter.GridFolder == view.Folders.Single(f => f.Path == "Characters").Id
            && chapter.GridFolder != characters.Id, "Folder layouts were not rebuilt.");
        Require(view.Folders.Single(f => f.Path == "").ItemOrder.SequenceEqual(ProjectLibrary.DefaultFolders.Select(name => "folder:" + name)), "The root order was lost.");

        await Expect(404, () => library.CreateAsync(new("Orphan", null, "Missing")));
        Require(!Directory.Exists(Path.Combine(root, "workspace", "Orphan")), "A missing template left an empty project behind.");
        await File.WriteAllTextAsync(Path.Combine(root, ".templates", "Bad.json"), "{\"documents\":[{\"path\":\"../escape.md\"}]}");
        Require(templates.List().Select(t => t.Name).SequenceEqual(["Default", "Novel"]), "An unsafe template should be skipped.");
        ExpectSync(400, () => templates.Save(new() { Name = "Unsafe", Documents = [new() { Path = ".odysseum/project.json" }] }));
        templates.Delete("Novel");
        ExpectSync(404, () => templates.Get("Novel"));
        templates.Delete("Default");
        Require(File.Exists(Path.Combine(root, ".templates", "Default.json")), "Deleting Default should restore the shipped one.");
    }),
    ("Lists dropped-in folders and ignores files, hidden, and metadata directories", async (root, library) =>
    {
        Directory.CreateDirectory(Path.Combine(root, "Dropped in"));
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        await File.WriteAllTextAsync(Path.Combine(root, "stray.md"), "Not a project");
        Require((await library.ListAsync()).Select(x => x.Slug).SequenceEqual(["Dropped in"]), "Only real project folders should be listed.");
        var view = await (await library.OpenAsync("Dropped in")).Services.GetProjectAsync();
        Require(view.Settings.Title == "Dropped in", "The folder name should become the working title.");
        Require(File.Exists(Path.Combine(root, "Dropped in", ".odysseum", "project.json")), "Opening should create metadata.");
    }),
    ("Rejects unsafe project names and reports missing projects", async (_, library) =>
    {
        await Expect(400, () => library.OpenAsync("../outside"));
        await Expect(400, () => library.OpenAsync(".odysseum"));
        await Expect(400, () => library.OpenAsync("nested/name"));
        await Expect(400, () => library.CreateAsync(new("   ", null)));
        await Expect(404, () => library.OpenAsync("Missing"));
    }),
    ("Keeps projects isolated and reuses one store per project", async (_, library) =>
    {
        await library.CreateAsync(new("One", null));
        await library.CreateAsync(new("Two", null));
        var one = (await library.OpenAsync("One")).Services;
        Require(ReferenceEquals(one, (await library.OpenAsync("One")).Services), "A project should open once per process.");
        await one.CreateAsync(new("Only here", "Manuscript", "Text"));
        var two = (await library.OpenAsync("Two")).Services;
        Require((await two.GetProjectAsync()).Documents.Count(Visible) == 1 && (await one.GetProjectAsync()).Documents.Count(Visible) == 2, "Documents leaked between projects.");
    }),
    ("Listing uses legacy manifest settings without writing and survives invalid metadata", async (root, library) =>
    {
        var directory = Path.Combine(root, "Legacy");
        Directory.CreateDirectory(Path.Combine(directory, ".odysseum"));
        var manifestPath = Path.Combine(directory, ".odysseum", "project.json");
        var id = Guid.NewGuid().ToString();
        var legacy = "{\"version\":1,\"id\":\"" + id + "\",\"title\":\"Legacy title\",\"wordGoal\":12345,\"documents\":{}}";
        await File.WriteAllTextAsync(manifestPath, legacy);
        var listed = (await library.ListAsync()).Single();
        Require(listed.Title == "Legacy title" && listed.Id == id, "Listing did not understand legacy metadata.");
        Require(await File.ReadAllTextAsync(manifestPath) == legacy, "Listing rewrote project metadata.");
        await File.WriteAllTextAsync(manifestPath, "{ broken metadata");
        listed = (await library.ListAsync()).Single();
        Require(listed.Title == "Legacy" && listed.Id == "", "Invalid metadata prevented listing the folder.");
        await Expect(422, () => library.OpenAsync("Legacy"));
        await File.WriteAllTextAsync(manifestPath, legacy);
        var opened = await library.OpenAsync("Legacy");
        Require((await opened.Services.GetProjectAsync()).Id == id, "A failed open leaked its instance lock or prevented retry.");
    }),
};

checks.AddRange([
    ("Versions keep the whole project, and a restore is itself a new version", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "First draft"));
        var external = Path.Combine(root, "Notes", "crlf.md");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Line one\r\nLine two\r\n")];
        await File.WriteAllBytesAsync(external, bytes);
        var named = await store.SaveVersionAsync("  Draft one ");
        Require(named.Name == "Draft one" && !named.Automatic, "A named version should carry its trimmed name.");
        await store.SaveAsync(scene.Document.Id, new("Second draft", scene.Document.Revision));
        await File.WriteAllTextAsync(external, "Rewritten\n");
        var extra = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        var automatic = await store.SaveAutomaticVersionAsync();
        Require(automatic is { Automatic: true, Name: null, Changes: >= 3 }, "An automatic version records every changed file.");
        Require(await store.SaveAutomaticVersionAsync() is null, "Nothing changed, so no version should be saved.");
        var versions = await store.GetVersionsAsync();
        Require(versions[0].Id == automatic!.Id && versions[1].Id == named.Id, "Versions list newest first.");
        Require((await File.ReadAllTextAsync(Path.Combine(root, ".git", "info", "exclude"))).Contains("history"), "Recovery snapshots must stay out of versions.");
        var restored = await store.RestoreVersionAsync(named.Id);
        Require(restored.Documents.All(d => d.Id != extra.Document.Id) && !File.Exists(Path.Combine(root, extra.Document.Path)), "A restore removes documents added since.");
        Require((await store.GetDocumentAsync(scene.Document.Id)).Content == "First draft", "A restore brings back the old prose.");
        Require((await File.ReadAllBytesAsync(external)).SequenceEqual(bytes), "Restored files must match byte for byte, BOM and CRLF included.");
        versions = await store.GetVersionsAsync();
        Require(versions[0].Name!.StartsWith("Restored") && versions[1].Id == automatic.Id, "A restore is a new version on top, not a rewind.");
        restored = await store.RestoreVersionAsync(versions[1].Id);
        Require(restored.Documents.Any(d => d.Id == extra.Document.Id) && (await store.GetDocumentAsync(scene.Document.Id)).Content == "Second draft", "Restoring the version before a restore undoes it.");
        await Expect(404, () => store.RestoreVersionAsync("0123456789abcdef0123456789abcdef01234567"));
        await Expect(404, () => store.RestoreVersionAsync("HEAD~1"));
        await Expect(400, () => store.SaveVersionAsync("  "));
    }),
]);

foreach (var (name, check) in checks)
{
    var root = Path.Combine(testRoot, passed.ToString());
    using var store = new ProjectServices(root, new ProjectEvents());
    await store.InitializeAsync();
    await check(root, store);
    passed++;
    Console.WriteLine($"PASS {name}");
}
foreach (var (name, check) in libraryChecks)
{
    var root = Path.Combine(testRoot, "library-" + passed);
    await using var library = new ProjectLibrary(root, new ProjectFactory(NullLoggerFactory.Instance, 300));
    await check(root, library);
    passed++;
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"\n{passed} storage checks passed. Fixtures: {testRoot}");

static bool Visible(DocumentSummary document) => document.Path.Split('/') is var parts && !(parts.Length >= 2 && parts[^1] == $".{parts[^2]}.md");
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void ExpectSync(int status, Action action)
{
    try { action(); }
    catch (WorkspaceException ex) when (ex.Status == status) { return; }
    throw new Exception($"Expected HTTP {status} rejection.");
}
static async Task Expect(int status, Func<Task> action)
{
    try { await action(); }
    catch (WorkspaceException ex) when (ex.Status == status) { return; }
    throw new Exception($"Expected HTTP {status} rejection.");
}

sealed class AllowingSettings : ISettingsProvider
{
    public IServerSettings GetSettings(bool copy = false) => new ServerSettings { AllowDeletingDefaultFolders = true };
    public void SaveSettings(IServerSettings settings) { }
    public void SaveSettings() { }
    public void DebugSettingsToLog() { }
}
