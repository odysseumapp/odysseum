# Server services

Names describe responsibilities. `Repository` reads and writes one kind of persistent data, `Service` is what
callers use to act on projects, folders and documents, `Codec` handles a document format, `Monitor` watches for
changes, `Events` distributes notifications, and `Factory` constructs a running project. Every repository and
every service has an interface, even when there is one implementation.

The layout follows Shoko Server: `Odysseum.Abstractions` holds the model interfaces (`IProject`, `IFolder`,
`IDocument`, `IDocumentVersion`), the service interfaces plugins call (`IProjectService`, `IFolderService`,
`IDocumentService`), the enums, event args, and `WorkspaceException`. `Odysseum.Server` holds the concrete
models in `Models/`, the repositories in `Repositories/`, the services in `Services/` and the HTTP layer in
`API/`. Plugins see services and models; they never see repositories.

| Component | Responsibility |
| --- | --- |
| `Repositories/Files/FileManager` | The only class that touches a project's disk: validates relative paths, refuses links, bounds reads to 4 MB, replaces files atomically, and acquires the instance lock. |
| `Repositories/ProjectManifestRepository` | Reads and migrates `project.json` and every `folder.json`, assembles the flat manifest, computes the project revision, and writes changed manifests through a transaction. |
| `Repositories/ManifestTransaction` | Journals multi-manifest writes and restores incomplete batches under the project lock. |
| `Repositories/DocumentVersionRepository` | Writes and lists Markdown recovery snapshots, deduplicated by hash. |
| `Repositories/ProjectVersionRepository` | Saves, lists, and restores whole-project versions in the git repository inside the project folder. Works on the root path with LibGit2Sharp. |
| `Repositories/DocumentRepository` | Scans a project into a snapshot, creates, saves and moves documents, and keeps every folder's own hidden document. |
| `Repositories/FolderRepository` | Creates, removes and lays out folders in the manifests. |
| `Repositories/ProjectRepository` | Opens projects by folder name through the library and saves project settings. |
| `Repositories/TemplateRepository`, `ThemeRepository` | Workspace-level JSON files for project templates and colour schemes. |
| `Models/Project`, `Folder`, `Document` | Immutable snapshots of an open project: settings, revision, the folder tree, documents with prose, links and link notes. |
| `Services/OpenProject` | One per open project. Owns the operation gate, the current snapshot, the repositories and the instance lock; rescans before every operation and publishes changes. |
| `Services/ProjectService` | `IProjectService`: lists, opens by folder name or UUID, creates from a template, saves settings. |
| `Services/FolderService` | `IFolderService`: creates, lays out and removes folders; guards the default folders. |
| `Services/DocumentService` | `IDocumentService`: creates, saves prose, updates details and links, moves, lists recovery snapshots. |
| `Services/OrderService` | `IOrderService`: the only code that reads or writes `itemOrder`. Arranges a folder's children, walks the whole project in manuscript order, and translates a flat reorder into per-folder lists. |
| `Services/ProjectLibrary` | Lists and creates project folders in the workspace; caches one open project per folder and funnels its events. |
| `Services/ProjectFactory`, `ProjectHandle` | Construct an `OpenProject` with its monitor; the handle stops the monitor before disposing the project. |
| `Services/Templates/ProjectTemplateService` | Captures a project as a template and applies one to a new project in a single manifest commit. |
| `Services/Documents/MarkdownDocumentCodec`, `DocumentRules` | Encoding, frontmatter, word counts; document extensions, folder classification and naming rules. |
| `Services/Monitoring/ProjectMonitor`, `ProjectEvents` | Requests scans after filesystem notifications and on a timer; saves a version once the project has been quiet. Publishes invalidations to the SSE controller. |
| `API/Views/ProjectViews`, `FolderPaths` | The API edge: builds the response records, computes each document's `order` as its position in the walk, translates folder IDs to and from `folder:Name` keys, and resolves request paths to folders. |
| `API/Middleware/ApiExceptionMiddleware` | Maps `WorkspaceError` to an HTTP status: Invalid 400, Forbidden 403, NotFound 404, Conflict 409, TooLarge 413, Corrupt 422, Unavailable 503. |
| `Bootstrap/DemoContent` | Seeds the sample project when enabled and the workspace is empty. |

## Snapshots and the gate

Services talk to documents and folders by ID and never see a path. A caller reads a project with
`IProjectService.GetAsync`, which takes the project's gate, rescans, and returns the current `Project` snapshot.
Snapshots are immutable: `Folder.Folders`, `Document.Links` and the rest are resolved once when the snapshot is
built, so reads need no lock. Every write takes an `IDocument` or `IFolder` from a snapshot plus the revision the
caller last saw, enters the gate, rescans, finds the same ID in the fresh snapshot, checks the revision, performs
the change, and returns objects from the new snapshot. A stale snapshot is harmless: the ID is looked up again and
the revision check rejects the write with a conflict.

Which revision a write checks: saving prose and moving a file check the document's revision (the SHA-256 of the
file); updating details and links, saving settings, folder layouts, folder removal and reordering check the
project's revision (the hash of every manifest and its path).

`OpenProject.RunAsync` is the one place operations serialize. Repositories never take the gate, so a service can
call several repository methods inside one operation; the template applier does this to write a new project's
files, rescan, and commit settings, titles and layouts once.

Metadata changes and scans work on a candidate manifest. The snapshot is replaced only after the manifest
transaction succeeds, so rejected requests, failed replacements, and incomplete scans cannot leave changes that a
later operation accidentally saves. Every scan is followed by `DocumentRepository.EnsureFolderDocumentsAsync`,
which gives any folder lacking its own `.Name.md` document one and rescans; this is the only write a read triggers.

## Ordering

There is one ordering. Each folder's `folder.json` keeps `itemOrder`, a list of its direct children's IDs,
documents and subfolders alike. Documents no longer carry an `order` number and folder entries no longer carry one
either; both are removed from existing manifests the first time a project is opened, with `folder:Name` keys
translated to IDs and unlisted children appended in their old order. `OrderService.ChildrenAsync` resolves the
list against the snapshot, ignores IDs that no longer name a child, and appends unlisted subfolders (the default
folders first at the root, then by name) and then unlisted documents by file name. A folder's own hidden document
is never a child. "All documents in order" is the walk from the root: a folder's hidden document first, then its
children in order, descending into subfolders.

Creating a document appends it to its folder's list in the same commit; moving one between folders takes its
entry along. The API still exposes `DocumentSummary.Order` and `folder:Name` keys in `FolderSummary.ItemOrder`;
`ProjectViews` computes the number as the position in the walk and translates the keys, and `PUT /order` becomes
`OrderService.ArrangeDocumentsAsync`, which sorts every folder's children by the first position any of their
documents holds in the flat list and commits all the lists in one transaction.

## Events

`ProjectEvents` still carries the SSE `workspace` event with its revision counter. Beside it, `OpenProject`
raises `Changed` whenever a scan or commit changes the snapshot fingerprint, and `DocumentsRemoved` and
`FoldersRemoved` for anything the new snapshot no longer has. `ProjectLibrary` funnels those from every open
project, and the services forward them as the `IProjectService`, `IDocumentService` and `IFolderService` events
plugins subscribe to, alongside the create, save, move and update events the services raise themselves.

## Listing, versions and templates

Project listing uses the manifest repository read-only: it does not persist migrations or start monitors, and
unreadable or invalid metadata falls back to the folder name until opening reports the error. `IProjectService.GetAsync`
accepts either the folder name or the project's UUID.

Versions are whole-project states kept as commits on one branch of a git repository at `<project>/.git`, made with
LibGit2Sharp. Opening a project with documents saves a version (a new project's first version is its template), the
monitor saves one after the project has been quiet for `VersionSeconds` (`ODYSSEUM_VERSION_SECONDS`, 60 by default,
0 for none), and `POST /api/projects/{project}/versions` saves a named one. A restore first saves the state being
replaced, writes the chosen version's files over the project, and records that as a new version. Recovery snapshots,
the instance lock, and transaction scratch are excluded through `.git/info/exclude`; the repository sets
`core.autocrlf` off so files round-trip byte for byte.

A template captures a project's goals, its folders with their layouts and arrangement (as `folder:Name` and
`document:File.md` keys), and its documents by path and title in walk order. Applying one writes the files, rescans,
and commits settings, titles, layouts and lists together.

HTTP routes, JSON response envelopes, document IDs, and the SSE `workspace` event name remain compatible with
existing clients. Links are one undirected relation between any two documents; a snapshot reads them from either
side. Markdown and manifest replacement are still separate filesystem operations, and arbitrary external editors
do not participate in the project's gate. See [the project format](project-format.md) for the files on disk.
