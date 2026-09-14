using System.Text;
using System.Text.Json.Nodes;
using Odysseum.Server.API.Enums;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Monitoring;
using Odysseum.Server.Settings;
using Microsoft.Extensions.Logging.Abstractions;

var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".test-data", "storage-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(testRoot);
var passed = 0;
var checks = new List<(string Name, Func<string, ProjectServices, Task> Run)>
{
    ("Reads an existing folder without rewriting its Markdown", async (root, store) =>
    {
        var path = Path.Combine(root, "existing.md");
        var original = Encoding.UTF8.GetBytes("# Existing\n\nText with **meaning**.\n");
        await File.WriteAllBytesAsync(path, original);
        var project = await store.GetProjectAsync();
        Require(project.Documents.Count == 1, "Document was not discovered.");
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
        Require(after.Documents.Single().Title == (results[1] ? "Updated document" : "Original"), "Rejected document metadata leaked into state.");
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
        var path = Path.Combine(root, ".writer", "project.json");
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
        var path = Path.Combine(root, ".writer", "project.json");
        await File.WriteAllTextAsync(path, "{ broken external edit");
        await Expect(422, () => store.SaveAsync(doc.Document.Id, new("New text", doc.Document.Revision)));
        Require(await File.ReadAllTextAsync(path) == "{ broken external edit", "Malformed metadata was replaced.");
    }),
    ("Blocks traversal and hidden internal paths", async (_, store) =>
    {
        await Expect(400, () => store.CreateAsync(new("Escape", "../outside", "No")));
        await Expect(400, () => store.CreateAsync(new("Escape", ".writer", "No")));
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
        var path = Path.Combine(root, ".writer", "project.json");
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
        Require(updated.Documents.Single(x => x.Id == scene.Document.Id).Characters.SequenceEqual([character.Document.Id]), "Attached characters were not stored.");
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, updated.Revision, [note.Document.Id])));
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
        var manifestPath = Path.Combine(root, ".writer", "project.json");
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
                && details.Characters.SequenceEqual([character.Document.Id]), "Rejected request changed the in-memory metadata.");
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
        Require(after.Revision == before.Revision && after.Documents.Count == 1,
            "An aborted scan left partial changes in the manifest.");
    }),
    ("Failed manifest replacements leave metadata, settings, and order unchanged", async (root, store) =>
    {
        // Windows denies replacement when another editor holds a handle without FileShare.Delete.
        if (!OperatingSystem.IsWindows()) return;
        var first = await store.CreateAsync(new("First", "Manuscript", "Text"));
        await store.CreateAsync(new("Second", "Manuscript", "Text"));
        var before = await store.GetProjectAsync();
        var manifestPath = Path.Combine(root, ".writer", "project.json");
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
            using (var folderLocked = new FileStream(Path.Combine(root, "Manuscript", ".writer", "folder.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
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
        var manifestPath = Path.Combine(root, "Manuscript", ".writer", "folder.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        manifest["documents"]![doc.Document.Id] = null;
        var invalid = manifest.ToJsonString();
        await File.WriteAllTextAsync(manifestPath, invalid);
        await Expect(422, () => store.GetProjectAsync());
        Require(await File.ReadAllTextAsync(manifestPath) == invalid, "Invalid metadata was rewritten.");
    }),
};

checks.AddRange([
    ("Arc positions are independent, validated, and preserved through ordinary metadata edits", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Scene prose"));
        var first = await store.CreateAsync(new("Race", "Arcs", "Arc notes only"));
        var second = await store.CreateAsync(new("Class", "Arcs", "Another thread"));
        Require(first.Document.Kind == DocumentKind.Arc, "Arc file was classified as a scene.");
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000,
            project.Revision, ArcPositions: new() { [first.Document.Id] = 4, [second.Document.Id] = 1 }));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000,
            project.Revision, ArcPositions: new() { [first.Document.Id] = 0, [second.Document.Id] = 1 }));
        var current = project.Documents.Single(d => d.Id == scene.Document.Id);
        Require(current.ArcPositions[first.Document.Id] == 0 && current.ArcPositions[second.Document.Id] == 1 && current.Order == scene.Document.Order,
            "Moving on one arc changed another arc or manuscript order.");
        foreach (var invalid in new[] { -1d, 10001d, double.NaN })
            await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Draft, 1000,
                project.Revision, ArcPositions: new() { [first.Document.Id] = invalid })));
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Draft, 1000,
            project.Revision, ArcPositions: new() { [scene.Document.Id] = 1 })));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Updated", "", DocumentStatus.Draft, 1000, project.Revision));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).ArcPositions.Count == 2, "Omitting arc positions removed attachments.");
        var folder = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", ".writer", "folder.json")))!;
        Require(folder["documents"]![scene.Document.Id]!["arcPositions"]![second.Document.Id]!.GetValue<double>() == 1, "Arc position was not persisted in the owning folder.");
        Require(!(await store.ExportAsync()).Contains("Arc notes only"), "Arc notes were included in manuscript export.");
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Updated", "", DocumentStatus.Draft, 1000,
            project.Revision, ArcPositions: new() { [second.Document.Id] = 1 }));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).ArcPositions.Count == 1, "Removing from one arc did not persist.");
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
        await File.WriteAllTextAsync(Path.Combine(root, ".writer", "project.json"), legacy);
        var project = await store.GetProjectAsync();
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".writer", "project.json")))!;
        Require(manifest["version"]!.GetValue<int>() == 2 && manifest["documents"]!.AsObject().Count == 0, "Root still owns nested documents.");
        Require(manifest["folders"]!.AsObject().Count == 3 && manifest["custom"]!.GetValue<string>() == "retain root", "Root folders or extension properties were lost.");
        var chapter = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 01", ".writer", "folder.json")))!;
        Require(chapter["documents"]![sceneId]!["path"]!.GetValue<string>() == "Arrival.md", "Chapter paths must be local filenames.");
        Require(chapter["documents"]![sceneId]!["custom"]!.GetValue<string>() == "retain document", "Document extension property was lost.");
        Require(File.Exists(Path.Combine(root, "Notes", "Empty", ".writer", "folder.json")), "Empty folders need manifests too.");
        var scene = project.Documents.Single(d => d.Id == sceneId);
        Require(scene.Title == "A different title" && scene.Synopsis == "Keep synopsis" && scene.Order == 7 && scene.Characters.SequenceEqual([characterId]), "Migration lost document metadata or links.");
        Require(await File.ReadAllTextAsync(Path.Combine(root, ".writer", "project.v1.json")) == legacy, "Legacy backup is not exact.");
        Require(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Chapter 01", "Arrival.md")) == prose, "Migration rewrote prose.");
        Require((await store.GetProjectAsync()).Revision == project.Revision, "Unchanged scans must not rewrite manifests.");
    }),
    ("Folder moves retain identity and file moves transfer metadata ownership", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript/First", "Text"));
        var before = await store.GetProjectAsync();
        await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Keep me", "", DocumentStatus.Revised, 50, before.Revision));
        var oldFolder = Path.Combine(root, "Manuscript", "First");
        var folderId = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(oldFolder, ".writer", "folder.json")))!["id"]!.GetValue<string>();
        Directory.Move(oldFolder, Path.Combine(root, "Manuscript", "Renamed"));
        var renamed = await store.GetDocumentAsync(scene.Document.Id);
        Require(renamed.Document.Path == "Manuscript/Renamed/Arrival.md" && renamed.Document.Synopsis == "Keep me", "Folder move lost metadata.");
        var parent = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", ".writer", "folder.json")))!;
        Require(parent["folders"]![folderId]!["path"]!.GetValue<string>() == "Renamed", "Parent did not track folder rename.");
        var moved = await store.MoveAsync(scene.Document.Id, new("Manuscript/Second/Arrival.md", renamed.Document.Revision));
        var source = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Renamed", ".writer", "folder.json")))!;
        var target = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "Manuscript", "Second", ".writer", "folder.json")))!;
        Require(source["documents"]!.AsObject().Count == 0 && target["documents"]![scene.Document.Id] is not null && moved.Document.Synopsis == "Keep me", "Move left duplicate metadata owners.");
    }),
    ("External folder metadata changes invalidate revisions and preserve extensions", async (root, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript/First", "Text"));
        var before = await store.GetProjectAsync();
        var path = Path.Combine(root, "Manuscript", "First", ".writer", "folder.json");
        var local = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        local["custom"] = "folder extension";
        local["documents"]![scene.Document.Id]!["synopsis"] = "From another editor";
        await File.WriteAllTextAsync(path, local.ToJsonString());
        await Expect(409, () => store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Stale", "", DocumentStatus.Draft, 1, before.Revision)));
        var current = await store.GetProjectAsync();
        Require(current.Documents.Single().Synopsis == "From another editor" && current.Revision != before.Revision, "Folder edits were not observed.");
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
        Require((await store.GetProjectAsync()).Documents.Single().Id == replacementId, "Replacement retained the wrong identity.");
        Require((await store.GetProjectAsync()).Documents.Single().Id == replacementId, "Retained metadata made the next scan invalid.");
        await File.WriteAllBytesAsync(path, bytes);
        Require((await store.GetDocumentAsync(original.Document.Id)).Document.Title == "Arrival", "Restoring the original lost its metadata.");
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
            folderPath = Path.Combine(recoveryRoot, "Manuscript", ".writer", "folder.json");
            original = await File.ReadAllBytesAsync(folderPath);
        }
        var backup = $".writer/manifest-transaction/{Guid.NewGuid():N}.bak";
        Directory.CreateDirectory(Path.Combine(recoveryRoot, ".writer", "manifest-transaction"));
        await File.WriteAllBytesAsync(Path.Combine(recoveryRoot, backup), original);
        var interrupted = JsonNode.Parse(original)!;
        interrupted["documents"]!.AsObject().First().Value!["title"] = "Uncommitted";
        var changed = Encoding.UTF8.GetBytes(interrupted.ToJsonString());
        await File.WriteAllBytesAsync(folderPath, changed);
        var journal = System.Text.Json.JsonSerializer.Serialize(new[] { new { Path = "Manuscript/.writer/folder.json", Backup = backup, After = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed)).ToLowerInvariant() } });
        await File.WriteAllTextAsync(Path.Combine(recoveryRoot, ".writer", "pending-manifests.json"), journal);
        using var reopened = new ProjectServices(recoveryRoot, new ProjectEvents());
        await reopened.InitializeAsync();
        Require((await reopened.GetProjectAsync()).Documents.Single().Title == "Arrival", "Incomplete batch was treated as committed.");
        var restored = await File.ReadAllBytesAsync(folderPath);
        Require(original.SequenceEqual(restored), "Recovery did not restore the exact original manifest.");
        Require(!File.Exists(Path.Combine(recoveryRoot, ".writer", "pending-manifests.json")), "Recovery journal was not cleared.");
    }),
    ("Locations behave like characters and validate scene links", async (_, store) =>
    {
        var scene = await store.CreateAsync(new("Arrival", "Manuscript", "Scene prose"));
        var place = await store.CreateAsync(new("Harbour", "Locations/Coast", "Location research"));
        var character = await store.CreateAsync(new("Mara", "Characters", "Biography"));
        Require(place.Document.Kind == DocumentKind.Location, "Nested location was classified as a scene.");
        var project = await store.GetProjectAsync();
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "", "", DocumentStatus.Draft, 1000, project.Revision, [character.Document.Id], [place.Document.Id, place.Document.Id]));
        Require(project.Documents.Single(d => d.Id == scene.Document.Id).Locations.SequenceEqual([place.Document.Id]), "Location link was not stored or deduplicated.");
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Done, 1, project.Revision, [], [character.Document.Id])));
        await Expect(400, () => store.UpdateMetadataAsync(scene.Document.Id, new("Rejected", "", "", DocumentStatus.Done, 1, project.Revision, Locations: Enumerable.Repeat(place.Document.Id, 201).ToArray())));
        project = await store.UpdateMetadataAsync(scene.Document.Id, new("Arrival", "Changed", "", DocumentStatus.Draft, 1000, project.Revision));
        var details = project.Documents.Single(d => d.Id == scene.Document.Id);
        Require(details.Locations.SequenceEqual([place.Document.Id]) && details.Characters.SequenceEqual([character.Document.Id]), "Omitted links or rejected request cleared attachments.");
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
        Require(File.Exists(Path.Combine(root, "My Novel", ".writer", "project.json")), "The project folder has no metadata.");
        var view = await (await library.OpenAsync("My Novel")).Services.GetProjectAsync();
        Require(view.Settings.Title == "My Novel" && view.Settings.WordGoal == 80000, "Title or goal was not stored.");
        await (await library.OpenAsync(odd.Slug)).Services.CreateAsync(new("Scene", "Manuscript", "Text"));
        Require((await library.ListAsync()).Select(x => x.Title).SequenceEqual(["Draft: Part 1", "My Novel", "My Novel"]), "Listing should show manifest titles, even with documents present.");
    }),
    ("Lists dropped-in folders and ignores files, hidden, and metadata directories", async (root, library) =>
    {
        Directory.CreateDirectory(Path.Combine(root, "Dropped in"));
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        await File.WriteAllTextAsync(Path.Combine(root, "stray.md"), "Not a project");
        Require((await library.ListAsync()).Select(x => x.Slug).SequenceEqual(["Dropped in"]), "Only real project folders should be listed.");
        var view = await (await library.OpenAsync("Dropped in")).Services.GetProjectAsync();
        Require(view.Settings.Title == "Dropped in", "The folder name should become the working title.");
        Require(File.Exists(Path.Combine(root, "Dropped in", ".writer", "project.json")), "Opening should create metadata.");
    }),
    ("Rejects unsafe project names and reports missing projects", async (_, library) =>
    {
        await Expect(400, () => library.OpenAsync("../outside"));
        await Expect(400, () => library.OpenAsync(".writer"));
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
        Require((await two.GetProjectAsync()).Documents.Count == 0 && (await one.GetProjectAsync()).Documents.Count == 1, "Documents leaked between projects.");
    }),
    ("Listing uses legacy manifest settings without writing and survives invalid metadata", async (root, library) =>
    {
        var directory = Path.Combine(root, "Legacy");
        Directory.CreateDirectory(Path.Combine(directory, ".writer"));
        var manifestPath = Path.Combine(directory, ".writer", "project.json");
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

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static async Task Expect(int status, Func<Task> action)
{
    try { await action(); }
    catch (WorkspaceException ex) when (ex.Status == status) { return; }
    throw new Exception($"Expected HTTP {status} rejection.");
}
