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
  Arcs/
    .odysseum/folder.json
    Race.md
  Beats/
    .odysseum/folder.json
    A promise is broken.md
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
      "characters": [],
      "locations": [],
      "arcPositions": {},
      "lastKnownHash": "sha256 of the complete file bytes"
    }
  }
}
```

The server discovers folders from disk and reconciles their parent indexes. Renaming or moving a folder with its manifest preserves its identity and document metadata. Moving an individual file transfers its metadata between owners. Copying a folder requires new folder and document UUIDs; duplicate manifest IDs block writes.

`characters` and `locations` contain UUIDs of linked character and location documents. The server validates the target kind. Omitting either list in an API metadata update leaves that list unchanged; sending `[]` clears it. Kind follows the top-level folder (case-insensitive): `Characters/` is character, `Locations/` is location, `Arcs/` is arc, `Beats/` is beat, `Notes/`, `Research/`, and `Story notes/` are notes; everything else is a scene. Only scenes contribute to manuscript progress and export.

A Beat is an ordinary Markdown document under `Beats/`, with its own prose, title, synopsis, and notes. Beats appear as points on arc timelines and are excluded from manuscript word counts and export. Create a Beat directly from an arc or attach an existing Beat; scenes, characters, locations, notes, and arc documents cannot be used as points. Legacy non-Beat arc links are hidden in API responses and the interface; their source documents and stored metadata are retained.

An arc is an ordinary Markdown document under `Arcs/`; its title names the timeline and its prose can hold planning notes. Only Beat documents attach using `arcPositions`, a map from arc UUID to a numeric position between 0 and 10000. For example, `{"arc-uuid": 3}` places a Beat at the fourth position on that arc. The interface numbers positions from 1. Position is independent for each arc and independent of manuscript order; gaps and coincident points are allowed. The timeline stacks coincident cards so neither is hidden. Removing a key detaches that Beat without deleting its file. Omitting `arcPositions` in a metadata update preserves existing attachments. Keys are validated against arc documents, and offline replay rewrites temporary arc IDs when the server assigns permanent ones.

`status` is `draft`, `revised`, or `done`. Document `order` retains the existing project-wide sequence used by the API, outline, and export, while each folder stores the positions of its own documents. Child folder entries have their own order values; the current API does not expose a separate folder-reordering operation. Reordering documents changes metadata without renaming files. `lastKnownHash` helps change detection and conservative legacy rename matching.

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
