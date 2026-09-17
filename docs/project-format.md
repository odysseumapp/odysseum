# Odysseum project format, version 2

## Workspace layout

The workspace (`ODYSSEUM_WORKSPACE`) is a directory whose immediate subdirectories are projects. Hidden directories (leading `.`), symbolic links/junctions, and files at the workspace root are ignored. A project's identifier in URLs and the API is its directory name; renaming the directory changes the address but not the project's UUID, so browser drafts keyed by that UUID survive.

Project directory names follow the same rules as document paths: no leading `.`, no trailing `.` or space, no path separators or characters the filesystem forbids. The application derives a name from the title when it creates a project and appends `-2`, `-3`, … to avoid collisions.

## Project directory

The project is a directory. Source documents are UTF-8 `.md`, `.markdown`, or `.txt` files; the application never requires an editor-specific JSON document format.

## Identity

New documents begin with:

```markdown
---
writer_id: 00d1e246-4f36-47e8-a308-000000000001
---

Your prose begins here.
```

The editor hides and preserves the complete opening frontmatter block. IDs are UUIDs, independent of names and paths. External renames retain the document's metadata when the ID is retained. Copying a file requires removing its `writer_id` or giving the copy a new UUID. Duplicate embedded IDs pause scanning and writes with an actionable error.

Files without IDs are accepted without rewriting. Odysseum associates them with their relative path in the manifest. A unique content-identical external rename can be inferred conservatively; a move plus edit cannot always be identified without an embedded ID. Moves through the application explicitly retain identity.

## Folder manifests

The project root has `.odysseum/project.json` (version 2). Every visible content folder, including empty folders, has its own `.odysseum/folder.json` (version 1). Internal `.odysseum` directories do not get manifests of their own. Projects written by earlier releases used `.writer`; those directories are renamed to `.odysseum` when the project is opened.

```text
My Novel/
  .odysseum/project.json
  Manuscript/
    .odysseum/folder.json
    Chapter 01/
      .odysseum/folder.json
      Arrival.md
  Characters/
    .odysseum/folder.json
    Mara.md
  Locations/
    .odysseum/folder.json
    Harbour.md
  Threads/
    .odysseum/folder.json
    Race.md
    Story Beats/
      .odysseum/folder.json
      Meet Cute.md
  Notes/
    .odysseum/folder.json
```

The root manifest contains project settings and indexes only its immediate files and folders:

```json
{
  "version": 2,
  "id": "fa1367f8-b351-47fc-82f2-0f8420d1ea31",
  "settings": { "title": "My Novel", "wordGoal": 60000, "defaultSceneWordGoal": 1000 },
  "documents": {},
  "folders": {
    "0ad487f0-00b8-42b3-9c58-c27cfa20bb6d": { "path": "Manuscript", "order": 0 }
  }
}
```

Each folder manifest has its own stable UUID, immediate child folder references, and metadata for its immediate documents. The UUID in a parent's `folders` dictionary matches the child's manifest ID. Paths are local names, never project-relative paths. For example, `Manuscript/Chapter 01/.odysseum/folder.json`:

```json
{
  "version": 1,
  "id": "02b8125e-e22d-45aa-bd9b-e35d954bca3e",
  "folders": {},
  "documents": {
    "00d1e246-4f36-47e8-a308-000000000001": {
      "path": "Arrival.md",
      "title": "Arrival",
      "synopsis": "A letter changes everything.",
      "notes": "Remember the lighthouse.",
      "status": "draft",
      "wordGoal": 1000,
      "order": 0,
      "links": [],
      "linkNotes": {},
      "lastKnownHash": "sha256 of the complete file bytes"
    }
  }
}
```

Every folder except the project root owns one hidden document named after it, `.Name.md` (for example `Manuscript/Chapter 01/.Chapter 01.md`). The server creates it when a folder is created or first seen, titles it after the folder, and gives it no word goal. It is an ordinary document — prose, synopsis, links, history — but the interface reaches it by opening the folder rather than listing it beside the folder's contents, and it never counts toward word goals or the export. A folder holding only its own document and metadata still counts as empty for removal. Other files beginning with a dot stay ignored, and a title beginning with a dot never produces a hidden file.

The server discovers folders from disk and reconciles their parent indexes. Renaming or moving a folder with its manifest preserves its identity and document metadata. Moving an individual file transfers its metadata between owners. Copying a folder requires new folder and document UUIDs; duplicate manifest IDs block writes.

`links` contains UUIDs of documents this one is linked to: characters, locations, threads, notes, other scenes — there is one kind of link. Links are undirected. The server writes both sides (a scene that links a character makes the character link the scene), and when reading it treats either side as sufficient, so a hand-written one-sided link still counts. An API metadata update sends the complete set; omitting `links` leaves them unchanged and `[]` clears them from both sides. The lists `characters`, `locations`, and `threads` from earlier releases are read as links and folded into `links` the next time the document's manifest is written. Offline replay rewrites temporary IDs when the server assigns permanent ones.

`linkNotes` is an optional object beside `links` holding a short note per link, keyed by the linked document's UUID: `"linkNotes": { "<uuid>": "Mara first doubts the map" }`. A note belongs to the link, not to one end of it. The server writes the same text on both documents, reads this document's entry before the other's (so a note written by hand on one side still shows on both), trims it, treats an empty note as none, and removes it from both sides when the link is removed. An API metadata update sends the complete set; omitting `linkNotes` leaves them unchanged, and a note for a document that is not linked is dropped. Notes are limited to 2000 characters.

Kind follows the top-level folder (case-insensitive): `Characters/` is character, `Locations/` is location, `Threads/` is thread, `Notes/`, `Research/`, and `Story notes/` are notes; everything else is a scene. Kinds only affect icons, grouping, and export: only scenes contribute to manuscript progress and export. A thread is an ordinary Markdown document anywhere under `Threads/`; its title names the thread and its prose holds planning notes.

Each folder's Grid view needs no setup: its rows are the folder's documents in order, grouped by subfolder, and its columns are every document of one other folder — Threads by default (Manuscript when you are in Threads). A mark sits where a row and a column are linked. The only saved choice is `gridFolder`, the UUID of the folder supplying the columns; `null` means the default, and a folder that no longer exists is rejected when the layout is saved. A pinned `threads` view from earlier releases becomes `grid`; its `threads`, `threadAxis` and `positions` keys are dropped on the next write.

`status` is `draft`, `revised`, or `done`. Document `order` retains the existing project-wide sequence used by the API, outline, and export. Child folder entries have their own order values; the current API does not expose a separate folder-reordering operation. Reordering documents changes metadata without renaming files. `lastKnownHash` helps change detection and conservative legacy rename matching.

Unknown root, folder, child folder, and document properties round-trip. Invalid JSON, unsafe local paths, and unsupported versions block saves instead of being replaced. Removed-file entries remain in surviving folders so restored documents can recover metadata. Removing a folder removes its metadata with it; back up the complete folder to preserve that information.

## Migration and revisions

Opening a version 1 project automatically distributes its centralized document metadata into the owning folders and upgrades the root to version 2. The original root bytes are retained in `.odysseum/project.v1.json` before migration. Markdown, document UUIDs, ordering, links, and history are preserved. Old top-level `title` and `wordGoal` settings are migrated into `settings`. Project listing is read-only and does not migrate files.

The API still returns a flat document list with project-relative paths. Its project revision is an opaque fingerprint of all current manifests and their paths, so an edit to any folder invalidates stale metadata writes. New projects without metadata receive manifests on first open.

Multi-manifest writes use flushed temporary files and a rollback journal at `.odysseum/pending-manifests.json`, with originals under `.odysseum/manifest-transaction/`. Removing the journal commits the batch. Failed writes restore the previous manifests; after a process interruption, the project lock owner recovers before scanning. Recovery refuses to overwrite an unrelated external edit. Keep the journal and its backups if recovery reports a conflict. Markdown saves and moves remain separate filesystem operations from metadata commits.

## Recovery

`.odysseum/history/<id>/` contains complete, readable document revisions, including frontmatter. Snapshot filenames consist of a UTC timestamp and a SHA-256 hash. The history viewer strips frontmatter for editing; the files themselves retain it. Snapshots include attempted saves and are not a definitive audit log of successful commits.

The browser keeps its copy of each opened project in IndexedDB (database `odysseum`): the project response, every document's server content and fingerprint, a queue of pending text edits (each recording the fingerprint it was written against), and an ordered queue of other operations (create, details, move, order, settings, create project). Edits are pushed with their fingerprint, so the server's revision check decides whether they apply cleanly or become a conflict; operations are replayed in order against the server's current state. Scenes and projects created offline carry temporary ids (`local-…`) until the server assigns real ones, at which point every local record naming them is re-keyed. The local copy belongs to that browser and is not a substitute for project backups; the workspace files remain the source of truth.

`.odysseum/instance.lock` is an application process lock, not project content. Back up the entire project including metadata and history; the lock file does not need to be backed up.

## Interoperability

Other editors can read the Markdown directly and ignore `.odysseum`. Optional metadata is plain JSON, suitable for a future SilverBullet or Obsidian adapter. Filesystem rescanning and revision-checked writes apply equally to edits from those applications. Interoperability does not imply automatic conflict-free simultaneous editing.

Word counts use runs of Unicode letters/numbers with internal apostrophes and hyphens. They are approximate drafting counts and may include words in Markdown destinations or code. Prose snapshots are the data; counts and views can be rebuilt.
