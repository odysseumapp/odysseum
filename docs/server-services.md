# Server services

Names describe responsibilities. A `Repository` keeps one kind of data, a `Service` holds the rules that callers use to
act on projects, a `StorageContext` reads and writes the stored data, a `Codec` handles a document format, a `Watcher`
reports changes made by other programs, and an `Emitter` sends changes to the browsers. Storage is behind one interface,
`IStorageContext`, so another storage (a database, for example) can replace the disk without a change to the repository
or the services.

`Odysseum.Abstractions` is the plugin contract: the server implements it, and plugins use it. It holds the item
interfaces (`IItem`, `IProjectItem`, `IProject`, `IFolder`, `IDocument`, `ILink`), the service interfaces
(`IProjectService`, `IHistoryService`), the request and result types (`ProjectSettings`, `FolderLayout`,
`DocumentDetails`, `FolderMoveResult`, `DocumentMoveResult`, `DocumentSearchResult`, `ProjectVersion`), the change types
(`Change`, `ChangeKind`, `ItemType`, `ChangesEventArgs`), the enums and `WorkspaceException`. `Odysseum.Server` holds the
records in `Models/`, the repository and the storage in `Repositories/`, the services in `Services/` and the HTTP layer
in `API/`. Plugins see the service interfaces and the item interfaces; they never see the records or the repository, so
they cannot make or change items except through the service.

## Words

- A **project name** is the project's folder name in the workspace, for example `my-novel`.
- A **project ID** is the UUID in the project's `project.json`. The project's top folder has the same ID.
- An **item** is a project, a folder, a document or a link. Each item has an `Id` and an `ETag`. Folders, documents and
  links also have a `ProjectId`.
- An **ETag** is a SHA-256 hash of the stored values of one item. Folder and document ETags include the path, so a move
  or a rename changes them.
- A **path** is relative to the project and uses `/`. The project's top folder has the empty path. Only the storage sets
  paths.

## Layers

```
  Controllers, ChangeEmitter, plugins
                 |
                 v
  IProjectService (ProjectService)      IHistoryService (HistoryService)
                 |                                 |
                 +----------------+----------------+
                                  v
                 IWorkspaceRepository (WorkspaceRepository)   reads from memory
                                  |
                                  v
                 IStorageContext (DiskStorageContext)         reads and writes the files
```

## Components

| Component | Responsibility |
| --- | --- |
| `Services/ProjectService` | `IProjectService`: the reads, the rules for every write to projects, folders, documents and links, search, Markdown export, and saving a project as a template. It passes on the repository's `Changed` batches. |
| `Services/ProjectTemplates` | Internal. Captures a project as a `ProjectTemplate`, and applies a template to a new project through `IProjectService`, so the same rules apply. |
| `Services/TreeOrder` | Static. The tree order of documents, and the documents among a folder's children. |
| `Services/HistoryService` | `IHistoryService`: named versions, version lists for a project or a document, reads and restores. It listens to the repository's `Changed` event and saves an automatic version after a project has had no changes for `VersionSeconds`. |
| `Repositories/IWorkspaceRepository`, `WorkspaceRepository` | One repository for the whole workspace, because the project is the aggregate. Four dictionaries in memory (projects, folders, documents, links), filled from `IStorageContext.Changed`. Reads never go to the disk; document text is not kept. Named writes, each with the ETag the caller last saw. Raises one `Changed` batch for each write or read of a project that changed something. |
| `Repositories/IStorageContext`, `Disk/DiskStorageContext` | Reads and writes the stored data of all projects. It keeps no copy of the data: each write reads what is stored now, checks the ETag, writes, and reports what changed as one `StorageChanges`. It also implements `IProjectLock`. |
| `Repositories/StorageChanges` | What one write or one read of a project changed: the items, the removed IDs, `ReplacesProject` for a full read, and `MovedIds` for the items that a move put in another folder. |
| `Repositories/Disk/SettingsFiles`, `Formats/*` | Read, check and write `project.json`, `folders.json`, `documents.json`, `links.json` and each `folder.json`. |
| `Repositories/Disk/ManifestTransaction` | Writes several settings files as one journaled batch that recovers when the project opens again. |
| `Repositories/Files/FileManager`, `Disk/OwnWrites` | `FileManager` is the only class that touches a project's disk: it checks paths, refuses links, limits reads, replaces files atomically and takes the instance lock. It records each path it changes in `OwnWrites`. |
| `Repositories/Disk/FileProjectWatcher` | `IProjectWatcher` over `FileSystemWatcher`: one watch per open project, a poll every `ScanSeconds` as a fallback, and a check against `OwnWrites`, so the server's own writes are not reported. |
| `Repositories/Git/GitProjectHistory` | `IProjectHistory` over LibGit2Sharp: one repository at `<project>/.git`, versions as commits. |
| `Repositories/TemplateRepository`, `ThemeRepository` | Project templates and colour themes, one JSON file each. |
| `Services/Views/ViewCatalog`, `DefaultViews`, `ViewNames` | The views module. The server stores view names and settings without reading them; `ViewNames` holds the rules for names and settings. `ViewCatalog` knows which settings hold a folder ID (`grid.columnFolder`): it checks them, clears them when the folder is deleted, and turns them into paths in templates. |
| `Services/Documents/MarkdownDocumentCodec`, `DocumentRules` | Encoding, front matter and word counts; document extensions, kinds and naming rules. |
| `Services/Projects/DefaultFolders`, `ProjectNames` | The folders every project starts with; the rules for project names. |
| `API/SignalR/ChangeEmitter`, `ProjectHub` | Sends the `changed` messages to the browsers. |
| `API/Middleware/ApiExceptionMiddleware` | Maps `WorkspaceError` to an HTTP status: Invalid 400, Forbidden 403, NotFound 404, Conflict 409, ETagMismatch 412, TooLarge 413, Corrupt 422, Unavailable 503. |
| `Bootstrap/DemoContent` | Copies the sample project into the workspace when the demo setting is on. |

## Reads

`IProjectService.GetAsync<T>(id)` takes `IProject`, `IFolder`, `IDocument` or `ILink`, and throws `NotFound` with a
message for each type. `GetAllAsync<T>(projectId)` takes `IFolder`, `IDocument` or `ILink` and returns them in no fixed
order. The type constraints (`T : IItem` and `T : IProjectItem`) stop a wrong type at compile time. Ordered reads have
names: `GetDocumentsInOrderAsync(projectId)` gives the tree order, and `GetChildrenAsync(folderId)` gives the documents
among the folder's children.

All reads come from the repository's memory, except `GetDocumentTextAsync`, search and export, which read the text from
the disk.

## Writes and ETags

Each write gives the ETag the caller last saw. The storage reads the stored item, compares the ETags, and refuses the
write with `ETagMismatch` when they are different. A stale caller is harmless.

The service checks the rules that need more than one item (for example, a folder moves only inside its own project, or a
default folder stays). The storage checks the rules about the files (for example, names, an empty folder, or a folder
that cannot move into itself). Each rule has one owner.

Most writes are one storage write. `IProjectLock` keeps other changes out when one task makes more than one write:
creating a project from a template, creating a document with a free file name, and deleting a folder and clearing view
settings that name it.

## Ordering

There is one ordering: `IFolder.ChildIds`, the subfolders and documents of a folder in order. On disk it is the
`itemOrder` list in each `folder.json`. Children that are missing from the stored list come after the listed ones:
subfolders first (in the top folder in the default order, otherwise by name), then documents by file name. A folder's own
hidden document is never a child.

A new folder or document goes at the end of its parent's children, in the same write. A move puts the item at an index
(clamped) among the target folder's children and saves the new order in the same write. A move inside the same folder
only changes the order. Tree order is a folder's own document first, then its children in order, with each subfolder's
documents in its place.

## Changes and events

Each write and each read of a project gives one `StorageChanges`. The repository puts the items in memory and makes one
`Change(Kind, Type, ProjectId, Id, ETag)` for each item that is new, has a new ETag, or is gone. Items that did not
change give no change, and a batch with no changes raises no event.

The kind comes from the write, not from the paths:

- **Added**: the item was not in memory.
- **Moved**: a move put the item in another folder.
- **Updated**: the item has a new ETag. A rename is Updated, and so are the documents inside a moved folder.
- **Removed**: the item is gone. Its ETag is null. Deleting a document also removes its links from memory; `links.json`
  keeps them, so they come back when a version brings the document back.

`ProjectService.Changed` passes on each batch. `ChangeEmitter` sends a `changed` message
`{ projectId, changes: [ { type, kind, id, etag } ] }` for each batch. The project changes go to all browsers, so a
project list sees projects that are added, updated or removed. The other changes go to the browsers that opened the
project through `ProjectHub.OpenProject`. These are thin notifications: the browser reads the items again.

## The watcher

`FileProjectWatcher` watches each open project folder. `FileManager` records each path it changes in `OwnWrites`, and
the watcher ignores an event for a path that is still in the state the server left it in. A change from another program
makes the storage read the project again; that read gives a `StorageChanges` with `ReplacesProject`, and the repository
reports only what changed. The watcher also reports every `ScanSeconds`, so a missed notification is caught late
instead of never.

## Versions and templates

Versions are whole-project states, kept as commits in a git repository at `<project>/.git`. `HistoryService` saves an
automatic version after a project has had no changes for `VersionSeconds` (`ODYSSEUM_VERSION_SECONDS`, 60 by default, 0
for none), and once at startup for each project that changed while the server was stopped. A restore saves the current
state first, writes the files of the version, and makes the repository read the project again. One document's history is
the list of versions that changed its file.

A template holds a project's word goals, its folders with their layouts and children (by name), and its documents by path
and title. View settings that hold a folder ID hold the folder's path in a template. `IProjectService.SaveAsTemplateAsync`
saves one, and `CreateProjectAsync` applies one through the same rules as any other write.

See [the project format](project-format.md) for the files on disk.
