# Odysseum project format, version 2

## Workspace layout

The workspace (`ODYSSEUM_WORKSPACE`) is a directory whose immediate subdirectories are projects. Hidden directories (leading `.`), symbolic links/junctions, and files at the workspace root are ignored. URLs and the API name a project by its UUID (the `id` in `project.json`); the directory name works too. Renaming the directory does not change the UUID, so addresses and browser drafts keyed by it survive.

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
  "itemOrder": ["0ad487f0-00b8-42b3-9c58-c27cfa20bb6d"],
  "folders": {
    "0ad487f0-00b8-42b3-9c58-c27cfa20bb6d": { "path": "Manuscript" }
  }
}
```

Each folder manifest has its own stable UUID, immediate child folder references, metadata for its immediate documents, and `itemOrder`, the order of its immediate children. The UUID in a parent's `folders` dictionary matches the child's manifest ID. Paths are local names, never project-relative paths. For example, `Manuscript/Chapter 01/.odysseum/folder.json`:

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
      "lastKnownHash": "sha256 of the complete file bytes"
    }
  }
}
```

Every folder except the project root owns one hidden document named after it, `.Name.md` (for example `Manuscript/Chapter 01/.Chapter 01.md`). The server creates it when a folder is created or first seen, titles it after the folder, and gives it no word goal. It is an ordinary document — prose, synopsis, links, history — but the interface reaches it by opening the folder rather than listing it beside the folder's contents, and it never counts toward word goals or the export. A folder holding only its own document and metadata still counts as empty for removal. Other files beginning with a dot stay ignored, and a title beginning with a dot never produces a hidden file.

The server discovers folders from disk and reconciles their parent indexes. Renaming or moving a folder with its manifest preserves its identity and document metadata. Moving an individual file transfers its metadata between owners. Copying a folder requires new folder and document UUIDs; duplicate manifest IDs block writes.

Links join two documents: characters, locations, threads, notes, other scenes — there is one kind of link, and it has no direction. `.odysseum/links.json` at the project root stores each link once, with one note that both ends read:

```json
{
  "version": 1,
  "links": [
    { "itemA": "00d1e246-4f36-47e8-a308-000000000001", "itemB": "7c1a5f0e-2d4b-4c8e-9a61-000000000002", "note": "Mara first doubts the map" }
  ]
}
```

The order of the list is the order in which each document lists its links; a new link goes at the end. In the API a document still has `links` (the UUIDs of the documents linked to it) and `linkNotes` (the note of each link, keyed by the other document's UUID). A metadata update sends the complete set; omitting `links` leaves them unchanged and `[]` removes them. Omitting `linkNotes` leaves the notes unchanged, a note for a document that is not linked is dropped, notes are trimmed, an empty note is none, and a note is at most 2000 characters. A link to a document whose file is gone stays in the file, so a restored file gets its links back. Earlier releases stored each link on both documents, as `links` and `linkNotes` in `folder.json`, and before that as `characters`, `locations`, and `threads` lists; a load reads all of these into `links.json` and the next write removes them from `folder.json`. Offline replay rewrites temporary IDs when the server assigns permanent ones.

Kind follows the top-level folder (case-insensitive): `Characters/` is character, `Locations/` is location, `Threads/` is thread, `Notes/`, `Research/`, and `Story notes/` are notes; everything else is a scene. Kinds only affect icons, grouping, and export: only scenes contribute to manuscript progress and export. A thread is an ordinary Markdown document anywhere under `Threads/`; its title names the thread and its prose holds planning notes.

The server does not know the views. A manifest's `pinnedView` is the name of the view the folder opens in, or `null`. `views` holds settings by view name, each a JSON object, and is omitted when no view has settings. A view name uses lowercase letters, digits, `.`, `-` and `_` and is at most 64 characters; a folder's settings are at most 64 KB. The server stores them without reading them, with one exception: the built-in views module (`Services/Views`) says which settings hold a folder UUID, and the server checks that the folder exists when the layout is saved, removes the setting when that folder is removed, and writes the folder's path instead of its UUID in project templates. The only such setting today is the Grid view's `columnFolder`: the folder whose documents are the grid's columns. Missing means the client's default.

```json
"pinnedView": "grid",
"views": { "grid": { "columnFolder": "0ad487f0-00b8-42b3-9c58-c27cfa20bb6d" } }
```

Earlier releases stored the column folder as `gridFolder`; a load reads it into `views.grid.columnFolder` and the next write drops `gridFolder`. A pinned `threads` view from earlier releases becomes `grid`; its `threads`, `threadAxis` and `positions` keys are dropped on the next write.

`status` is `draft`, `revised`, or `done`. `lastKnownHash` helps change detection and conservative legacy rename matching.

There is one ordering. `itemOrder` lists the UUIDs of the folder's immediate children, documents and subfolders alike, in the order they appear; the folder's own hidden document is never listed. Children missing from the list come after the listed ones: subfolders first (at the root in the default order, otherwise by name), then documents by file name. The manuscript order the API, outline, and export use is the walk from the root: each folder's hidden document, then its children in order, descending into subfolders. Reordering changes `itemOrder` without renaming files; the application does it by moving one item to an index in a folder, which also moves documents and folders between folders. Manifests written by earlier releases carried a per-document `order` number, a per-subfolder `order` number, and `folder:Name` keys in `itemOrder`; opening the project translates the keys to UUIDs, appends unlisted children in their old order, and drops the numbers.

Unknown root, folder, child folder, and document properties round-trip. Invalid JSON, unsafe local paths, and unsupported versions block saves instead of being replaced. Removed-file entries remain in surviving folders so restored documents can recover metadata. Removing a folder removes its metadata with it; back up the complete folder to preserve that information.

## Project templates

A project template is what a new project starts with. Templates live beside the settings file in `templates/` (`ODYSSEUM_TEMPLATES`), one JSON file each, and belong to the workspace rather than to any project; the file name is the template's name. They are separate from document kinds, which still follow the top-level folder.

A template is made by saving an existing project as one (`PUT /api/templates/{name}` with `{ "project": "<project UUID or folder name>" }`), and chosen when a project is created (`POST /api/projects` with `"template": "<name>"`). It records the project's word goals, every folder with its layout, and which documents exist, by path and title. What a document holds (prose, synopsis, notes, links, status, history) is not part of a project template: a project made from one gets empty documents with fresh UUIDs. Folders' own hidden documents are not recorded; every folder gets one anyway.

```json
{
  "name": "Default",
  "settings": { "wordGoal": 50000, "defaultSceneWordGoal": 1000 },
  "folders": [
    { "path": "", "children": ["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"] },
    { "path": "Manuscript", "children": ["Chapter 01"] },
    { "path": "Manuscript/Chapter 01", "pinnedView": "board", "children": ["Scene 01.md"], "views": { "grid": { "columnFolder": "Threads" } } }
  ],
  "documents": [
    { "path": "Manuscript/Chapter 01/Scene 01.md", "title": "Scene 01" }
  ]
}
```

The project root is the folder with the empty path. `children` are the names of a folder's subfolders and documents (file names) in order; a folder and a file in one folder cannot have the same name, so a name is enough. In `views`, a setting that holds a folder holds its path. `documents` are in manuscript order; each takes `defaultSceneWordGoal`. Templates from earlier releases, with `itemOrder` (`folder:Name` and `document:File.md` keys) and `gridFolder`, still load. A template whose paths could not be written into a project is refused when saved and skipped when listed.

`Default` always exists. The server writes `Default.json` at startup when it is missing — the six default folders, `Manuscript/Chapter 01`, and an empty `Scene 01` — and deleting it restores that file. Saving a project over `Default` changes what new projects start with.

## Migration and revisions

Opening a version 1 project automatically distributes its centralized document metadata into the owning folders and upgrades the root to version 2. The original root bytes are retained in `.odysseum/project.v1.json` before migration. Markdown, document UUIDs, ordering, links, and history are preserved. Old top-level `title` and `wordGoal` settings are migrated into `settings`. Project listing reads only each project's `project.json` and does not migrate files. A project folder that was never opened has no `project.json`, so the list opens it once to give it its UUID.

The API returns a flat document list with project-relative paths, and each folder's `children` as IDs in order. Its project revision is an opaque fingerprint of all current manifests and their paths, so an edit to any folder invalidates stale metadata writes. New projects without metadata receive manifests on first open.

Multi-manifest writes use flushed temporary files and a rollback journal at `.odysseum/pending-manifests.json`, with originals under `.odysseum/manifest-transaction/`. Removing the journal commits the batch. Failed writes restore the previous manifests; after a process interruption, the project lock owner recovers before scanning. Recovery refuses to overwrite an unrelated external edit. Keep the journal and its backups if recovery reports a conflict. Markdown saves and moves remain separate filesystem operations from metadata commits.

## Recovery

The browser keeps its copy of each opened project in IndexedDB (database `odysseum`): the project response, every document's server content and fingerprint, a queue of pending text edits (each recording the fingerprint it was written against), and an ordered queue of other operations (create, details, move, settings, create project). Edits are pushed with their fingerprint, so the server's revision check decides whether they apply cleanly or become a conflict; operations are replayed in order against the server's current state. Scenes and projects created offline carry temporary ids (`local-…`) until the server assigns real ones, at which point every local record naming them is re-keyed. The local copy belongs to that browser and is not a substitute for project backups; the workspace files remain the source of truth.

`.git` holds the project's versions: every file of the project as it was at each automatic or named save. It is an
ordinary git repository, so any git tool can read it, but only the server writes it. It is also the history of each
document: the versions that changed a file, and the file as it was in each of them. The instance lock and the
transaction scratch are not part of a version.

`.odysseum/instance.lock` is an application process lock, not project content. Back up the entire project including metadata and `.git`; the lock file does not need to be backed up.

## Interoperability

Other editors can read the Markdown directly and ignore `.odysseum`. Optional metadata is plain JSON, suitable for a future SilverBullet or Obsidian adapter. Filesystem rescanning and revision-checked writes apply equally to edits from those applications. Interoperability does not imply automatic conflict-free simultaneous editing.

Word counts use runs of Unicode letters/numbers with internal apostrophes and hyphens. They are approximate drafting counts and may include words in Markdown destinations or code. Prose snapshots are the data; counts and views can be rebuilt.
