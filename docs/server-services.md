# Server services

Names describe responsibilities. A `Repository` reads and writes one kind of persistent data, a `Service` is what
callers use to act on projects, folders and documents, a `Codec` handles a document format, a `Watcher` reports changes
made by other programs, `Events` distributes notifications, and a `Session` holds one open project. Storage sits behind
three interfaces, so another storage (a database, for example) can replace the disk without a change to the services.

The layout follows Shoko Server: `Odysseum.Abstractions` holds the model interfaces (`IItem`, `IProject`, `IFolder`,
`IDocument`), the service interfaces plugins call (`IProjectService`, `IFolderService`, `IDocumentService`,
`IHistoryService`), `ProjectBranch`, `ProjectInfo`, `ProjectVersion`, the enums, event args, and `WorkspaceException`.
`Odysseum.Server` holds the concrete models in `Models/`, the editors in `Models/Editing/`, the storage interfaces and
their implementations in `Repositories/`, the sessions and services in `Services/` and the HTTP layer in `API/`.
Plugins see services and models; they never see repositories.

## Words

- A **project name** is the project's folder name in the workspace, for example `my-novel`. It is the name in URLs.
- A **project branch** (`ProjectBranch`) is a project name together with a branch name. Every storage call takes one.
  Only the `main` branch exists today; `CreateBranchAsync` and `MergeAsync` report that they are not supported.
- An **item** is a folder or a document. Its parent and its position come from the tree, never from a stored number.

## Components

| Component | Responsibility |
| --- | --- |
| `Repositories/IProjectRepository` | Loads and saves projects: `ListAsync`, `ExistsAsync`, `CreateAsync(title)`, `OpenAsync(branch)`, `LoadAsync(branch)`, `LoadBodyAsync(branch, id)`, `SaveAsync(branch, changes, revision)`. No paths, streams or storage types cross it. |
| `Repositories/IProjectHistory` | Saved versions: `SaveVersionAsync`, `ListVersionsAsync(branch, path?)`, `RestoreAsync(branch, id, path?)`, `ReadAsync`, and the not-yet-supported `CreateBranchAsync` and `MergeAsync`. |
| `Repositories/IProjectWatcher` | Raises `Changed(branch)` when another program changes a watched project. The server's own writes are not reported. |
| `Repositories/Disk/DiskProjectRepository` | Projects as folders: scans the Markdown files, reads and writes `project.json` and every `folder.json`, keeps every folder's own hidden document, and turns one `Changes` into file writes, moves, folder creation and removal, and one manifest transaction. |
| `Repositories/Disk/ManifestFiles`, `ManifestTransaction` | Read, validate and migrate the manifests; compute the project revision; write several manifests as one journaled batch that recovers on reopen. |
| `Repositories/Disk/OwnWrites`, `Files/FileManager` | `FileManager` is the only class that touches a project's disk: validates paths, refuses links, bounds reads to 4 MB, replaces files atomically, acquires the instance lock. It records every path it changes, and the state it left it in, in `OwnWrites`. |
| `Repositories/Disk/FileProjectWatcher` | `IProjectWatcher` over `FileSystemWatcher`: one watch per open project, a 200 ms debounce, a poll every `ScanSeconds` as a fallback, and a check against `OwnWrites` so the server's own writes are skipped. |
| `Repositories/Git/GitProjectHistory` | `IProjectHistory` over LibGit2Sharp: one repository at `<project>/.git`, versions as commits, per-document history through the commit log of one path, per-document restore through a checkout of one path. |
| `Models/Item`, `Folder`, `Document` | Immutable. `Item` has `Id`, `Name`, `ParentId`, `OrderInParent` and `Path`; `Folder` has `Children` in order and its `OwnDocument`; `Document` has its details, links, `Revision`, `Modified`, `WordCount` and a `Body` that is null until opened. |
| `Models/Project`, `ProjectData` | `ProjectData` is what storage loads and saves: settings, the tree with document summaries, the revision. `Project` places the tree: it sets `ParentId`, `OrderInParent` and `Path` on a copy of every item. This is the only place those are set. `Project.Walk()` is the manuscript order. |
| `Models/Changes` | What one save changes: settings, the touched folders and documents (placed copies), and removed folder IDs. Storage derives creation, move and body write from them. |
| `Models/Editing/FolderEditor`, `DocumentEditor` | Edit a copy of a project and mark what changed. `FolderEditor.Move(item, folder, index)` is the only way to change the order. `DocumentEditor.SetDetails` mirrors links and link notes on the other documents. |
| `Services/Projects/ProjectSession` | One open project branch. Holds `Current : Project` and runs every operation under one lock; an operation reloads the project first, so it works on what is stored now. `SaveAsync` makes the saved state current. Raises `Changed`, `DocumentsRemoved`, `FoldersRemoved` and publishes to `ProjectEvents` when the fingerprint changed. |
| `Services/Projects/ProjectSessions` | The open sessions, one per project branch: `Get`, `OpenAsync`, `CloseAsync`, `ListAsync`, `CreateAsync` (from a template). Reloads a session when the watcher reports a change. Saves a version when a project with documents opens and when a project is created. |
| `Services/ProjectService` | `IProjectService`: lists, opens by branch or by folder name or UUID, creates from a template, saves settings. |
| `Services/FolderService` | `IFolderService`: creates, lays out, moves and removes folders and moves documents; guards the default folders. |
| `Services/DocumentService` | `IDocumentService`: opens (loads the body), creates, saves prose, updates details and links, moves. |
| `Services/HistoryService` | `IHistoryService`: named versions, per-document version lists, reads and restores; saves an automatic version after a project has been quiet for `VersionSeconds`. |
| `Services/Projects/ProjectEvents`, `ProjectNames`, `DefaultFolders`, `ProjectBranchComparer` | The SSE channel; the project name rules; the folders every project starts with; project names compared as the file system compares them. |
| `Services/Templates/ProjectTemplateService` | Captures a project as a template and applies one to a new project in a single save through the editors. |
| `Services/Documents/MarkdownDocumentCodec`, `DocumentRules` | Encoding, frontmatter, word counts; document extensions, folder classification and naming rules. |
| `API/Views/ProjectViews`, `FolderPaths` | The API edge: builds the response records, computes each document's `order` as its position in the walk, and resolves request paths to folders. |
| `API/Middleware/ApiExceptionMiddleware` | Maps `WorkspaceError` to an HTTP status: Invalid 400, Forbidden 403, NotFound 404, Conflict 409, TooLarge 413, Corrupt 422, Unavailable 503. |
| `Bootstrap/DemoContent` | Seeds the sample project when enabled and the workspace is empty. |

## Sessions and the lock

Services address a project by its `ProjectBranch` and its items by ID; they never see a path. `IProjectService.GetAsync`
opens the session, reloads the project under the lock, and returns the current `Project`. A `Project` does not change:
`Folder.Children`, `Document.Links` and the rest are set once when it is built, so reads need no lock.

Every write takes the branch, an ID and the revision the caller last saw. The service enters the session lock, reloads,
checks the revision, edits a copy with a `FolderEditor` or `DocumentEditor`, and passes the editor's `Changes` to
`ProjectSession.SaveAsync`, which hands them to `IProjectRepository.SaveAsync` together with the revision after the
reload. The repository checks that revision again against what is stored, applies the changes, and returns the new
state, which becomes `Current`. A stale caller is harmless: the ID is looked up again and the revision check rejects the
write with a conflict.

Which revision a write checks: saving prose and moving a document check the document's revision (the SHA-256 of the
file); updating details and links, saving settings, folder layouts, folder removal and moves check the project's
revision (the hash of every manifest and its path).

`ProjectSession.RunAsync` is the one place operations serialize. History calls run under the same lock, so git work
never overlaps a save. Every load is followed by the creation of any folder's missing own `.Name.md` document; this is
the only write a read makes.

## Ordering

There is one ordering: `Folder.Children`, the subfolders and documents of a folder in order. On disk it is the
`itemOrder` list of child IDs in each `folder.json`. `Project` sets `OrderInParent` on every item from its position in
`Children`. Children missing from the stored list come after the listed ones: subfolders first (at the root in the
default order, otherwise by name), then documents by file name. A folder's own hidden document is never a child.
"All documents in order" is `Project.Walk()`: a folder's own document first, then its children in order, descending into
subfolders.

The only way to change the order is `FolderService.MoveAsync(branch, itemId, targetFolderId, index, revision)`. It
places the item at that index among the target folder's children, moving it between folders when the target is not its
parent. Documents move with their file and metadata; folders move with their directory and keep their ID. Creating a
document or folder appends it to its parent's children in the same save. The API exposes this as
`PUT /api/projects/{project}/folders/move`; `FolderSummary.children` lists the IDs in order and `DocumentSummary.order`
is the position in the walk.

## Events and the watcher

`ProjectEvents` carries the SSE `workspace` event with its revision counter. `ProjectSession` publishes to it and raises
`Changed`, `DocumentsRemoved` and `FoldersRemoved` whenever a reload or a save changes the project fingerprint.
`ProjectSessions` forwards those from every open session, and the services forward them as the `IProjectService`,
`IDocumentService` and `IFolderService` events plugins subscribe to, beside the create, save, move and update events the
services raise themselves.

`FileProjectWatcher` watches each open project folder. `FileManager` records every path it changes in `OwnWrites`,
before the change and again after it with the state it left the path in. The watcher drops an event for a recorded path
that is still in that state, so the server's own writes are not reported; an edit from outside changes the state and is
reported even right after a save. A reported change makes `ProjectSessions` reload the session under its lock; a reload
that finds the same fingerprint publishes nothing. The watcher also reports every `ScanSeconds`, so a missed
notification is caught late instead of never.

## Listing, versions and templates

Project listing uses the manifests read-only: it does not persist migrations or open sessions, and unreadable or invalid
metadata falls back to the folder name until opening reports the error. `IProjectService.GetAsync(string)` accepts either
the folder name or the project's UUID.

Versions are whole-project states kept as commits on one branch of a git repository at `<project>/.git`, made with
LibGit2Sharp. Opening a project with documents saves a version, a new project's first version is its template,
`HistoryService` saves one after the project has been quiet for `VersionSeconds` (`ODYSSEUM_VERSION_SECONDS`, 60 by
default, 0 for none), and `POST /api/projects/{project}/versions` saves a named one. A restore first saves the state being
replaced, writes the chosen version's files over the project, and records that as a new version. One document's history
is the list of versions that changed its file (`GET .../documents/{id}/versions`); one version of it can be read
(`GET .../versions/{version}`) or restored on its own (`POST .../versions/{version}/restore`), which also saves the state
being replaced first. The instance lock and transaction scratch are excluded through `.git/info/exclude`; the repository
sets `core.autocrlf` off so files round-trip byte for byte.

A template captures a project's goals, its folders with their layouts and arrangement (as `folder:Name` and
`document:File.md` keys), and its documents by path and title in walk order. Applying one creates the folders and
documents through the editors and saves settings, titles, layouts and order together.

Links are one undirected relation between any two documents; a load reads them from either side. Markdown and manifest
replacement are still separate filesystem operations, and arbitrary external editors do not participate in the session
lock. See [the project format](project-format.md) for the files on disk.

## API changes in this design

| Before | After |
| --- | --- |
| `GET /api/projects` item: the field that holds the project folder name | `name` |
| `FolderSummary.itemOrder` (`folder:Name` keys and IDs) | `children` (IDs) |
| `PUT folders/layout` with `itemOrder` | `PUT folders/layout` without it; `PUT folders/move` `{ id, targetFolder, index, revision }` |
| `PUT /api/projects/{project}/order` | removed; use `folders/move` |
| `GET documents/{id}/snapshots[/{snapshot}]` | `GET documents/{id}/versions[/{version}]`, `POST documents/{id}/versions/{version}/restore` |
