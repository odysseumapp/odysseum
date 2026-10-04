# Class diagram

UML class diagrams of the server, in ASCII. Members that only help inside a class are left out. See
[server services](server-services.md) for what each part does.

Legend:

```
  A --|> B     A inherits from B
  A ..|> B     A implements interface B
  A --> B      A uses B (holds it or calls it)
  A <*>-- B    A contains B; B does not exist without A
  <<interface>>, <<record>>, <<static>>, <<internal>> mark the kind of type
```

## 1. Abstractions: the plugin contract

`Odysseum.Abstractions` holds what plugins see. The server implements it. The services take and return the item
interfaces, never the records, so a plugin cannot make or change an item except through a service.

```
                     +-------------------------------+
                     | <<interface>> IItem           |
                     |-------------------------------|
                     | Id : string                   |
                     | ETag : string                 |
                     +-------------------------------+
                        ^                       ^
                        :                       :
  +-------------------------------+   +-------------------------------+
  | <<interface>> IProject        |   | <<interface>> IProjectItem    |
  |-------------------------------|   |-------------------------------|
  | Name, Title : string          |   | ProjectId : string            |
  | WordGoal : int                |   +-------------------------------+
  | DefaultSceneWordGoal : int    |                   ^
  | RootFolderId : string         |                   :
  | LastModified : DateTimeOffset |     +-------------+-------------+
  | Warning : string?             |     :             :             :
  +-------------------------------+     :             :             :
                                        :             :             :
+--------------------------------------+   +-------------------------------+   +----------------------------+
| <<interface>> IFolder                |   | <<interface>> IDocument       |   | <<interface>> ILink        |
|--------------------------------------|   |-------------------------------|   |----------------------------|
| Name : string                        |   | FolderId, Name : string       |   | FirstDocumentId : string   |
| ParentFolderId : string?             |   | Kind : DocumentKind           |   | SecondDocumentId : string  |
| ChildIds : string[]       (in order) |   | IsFolderDocument : bool       |   | Note : string              |
| OwnDocumentId : string?              |   | Title, Synopsis, Notes        |   +----------------------------+
| PinnedView : string?                 |   | Status : DocumentStatus       |
| Views : Dict<string, JsonElement>    |   | WordGoal, WordCount : int     |
+--------------------------------------+   | LastModified : DateTimeOffset |
                                           +-------------------------------+

+--------------------------------------------------------------------------+
| <<interface>> IProjectService                                            |
|--------------------------------------------------------------------------|
| Changed : event ChangesEventArgs                                         |
|--------------------------------------------------------------------------|
| GetAsync<T : IItem>(id) : T            (IProject, IFolder, IDocument,    |
|                                         ILink; NotFound when missing)    |
| GetAllAsync<T : IProjectItem>(projectId) : T[]     (no fixed order)      |
| GetProjectsAsync() : IProject[]        (by title, finds new folders)     |
| GetDocumentsInOrderAsync(projectId) : IDocument[]  (tree order)          |
| GetChildrenAsync(folderId) : IDocument[]                                 |
| GetLinksForDocumentAsync(documentId) : ILink[]                           |
| GetDocumentTextAsync(documentId) : string                                |
| SearchDocumentsAsync(projectId, text) : DocumentSearchResult[]           |
| ExportMarkdownAsync(projectId) : string                                  |
|--------------------------------------------------------------------------|
| CreateProjectAsync(title, wordGoal?, templateName?) : IProject           |
| UpdateProjectSettingsAsync(projectId, settings, etag) : IProject         |
| SaveAsTemplateAsync(projectId, name)                                     |
| CreateFolderAsync(parentFolderId, name) : IFolder                        |
| UpdateFolderLayoutAsync(folderId, layout, etag) : IFolder                |
| MoveFolderToFolderAsync(folderId, targetId, index, etag)                 |
|   : FolderMoveResult                                                     |
| DeleteFolderAsync(folderId, etag)                                        |
| CreateDocumentAsync(folderId, title, text?) : IDocument                  |
| UpdateDocumentTextAsync(documentId, text, etag) : IDocument              |
| UpdateDocumentDetailsAsync(documentId, details, etag) : IDocument        |
| RenameDocumentAsync(documentId, name, etag) : IDocument                  |
| MoveDocumentToFolderAsync(documentId, targetId, index, etag)             |
|   : DocumentMoveResult                                                   |
| CreateLinkAsync(firstId, secondId, note) : ILink                         |
| UpdateLinkNoteAsync(linkId, note, etag) : ILink                          |
| DeleteLinkAsync(linkId, etag)                                            |
+--------------------------------------------------------------------------+

+------------------------------------------------+   +------------------------------------------------+
| <<interface>> IHistoryService                  |   | <<record>> Change                              |
|------------------------------------------------|   |------------------------------------------------|
| SaveVersionAsync(projectId, name)              |   | Kind : ChangeKind  (Added, Updated, Moved,     |
| GetVersionsByProjectIdAsync(projectId)         |   |                     Removed)                   |
| GetVersionsByDocumentIdAsync(documentId)       |   | Type : ItemType    (Project, Folder, Document, |
| GetDocumentTextFromVersionAsync(docId, verId)  |   |                     Link)                      |
| RestoreProjectVersionAsync(projectId, verId)   |   | ProjectId, Id : string                         |
| RestoreDocumentVersionAsync(docId, verId)      |   | ETag : string?          (null when Removed)    |
+------------------------------------------------+   +------------------------------------------------+
                                                       ChangesEventArgs holds the Change list of one batch.

  Request and result types: ProjectSettings, FolderLayout, DocumentDetails, FolderMoveResult, DocumentMoveResult,
  DocumentSearchResult, ProjectVersion. Errors: WorkspaceException with a WorkspaceError.
```

## 2. Models: the records

Immutable records. The storage makes them; a write makes a copy with `with`. `Path` is set only by the storage.

```
  Project  ..|> IProject     (Id, Name, Title, WordGoal, DefaultSceneWordGoal, ETag, LastModified, Warning;
                              RootFolderId = Id)
  Folder   ..|> IFolder      (+ Path, IsRoot)
  Document ..|> IDocument    (+ Path)
  Link     ..|> ILink        (+ Joins(documentId), Joins(first, second))
  <<static>> ProjectPaths    Join, ParentOf, NameOf, IsInside
```

## 3. Services

```
  +----------------------------------------------+         +----------------------------------------------+
  | ProjectService            ..|> IProjectService|         | HistoryService            ..|> IHistoryService|
  |----------------------------------------------|         |----------------------------------------------|
  | Changed : event   (passes on the batches)    |         | StartAsync()     (versions at startup, timer)|
  +----------------------------------------------+         +----------------------------------------------+
     |        |         |          |         |                 |               |              |
     |        |         |          |         +--> ViewCatalog  |               |              +--> IProjectHistory
     |        |         |          +--> ITemplateRepository     |               +--> IProjectLock
     |        |         +--> ISettingsProvider                 +--> IWorkspaceRepository (Changed)
     |        +--> IProjectLock
     +--> IWorkspaceRepository
     |
     +--> <<internal>> ProjectTemplates            +--> <<static>> TreeOrder
          |-----------------------------|               |-------------------------------------|
          | CaptureAsync(project, name) |               | Documents(root, folders, documents) |
          | ApplyAsync(project, template)|              | Children(folder, documents)         |
          +-----------------------------+               +-------------------------------------+
            uses IProjectService and IWorkspaceRepository
```

## 4. Repository and storage

```
+----------------------------------------------------------+
| <<interface>> IWorkspaceRepository                       |
|----------------------------------------------------------|
| Changed : event ChangesEventArgs                         |
|----------------------------------------------------------|
| GetAsync<T>(id) : T?       (Project, Folder, Document,   |
|                             Link)                        |
| GetAllAsync<T>(projectId) : T[]   (Folder, Document,     |
|                                    Link; no fixed order) |
| GetProjectsAsync() : Project[]                           |
| GetLinksForDocumentAsync(documentId) : Link[]            |
|----------------------------------------------------------|
| AddProjectAsync, UpdateProjectAsync,                     |
|   FindNewProjectsAsync, ReloadProjectAsync               |
| AddFolderAsync, UpdateFolderAsync,                       |
|   MoveFolderAsync(id, targetParentId, index, etag),      |
|   DeleteFolderAsync                                      |
| AddDocumentAsync(document, text), UpdateDocumentAsync,   |
|   MoveDocumentAsync(id, targetFolderId, index, etag),    |
|   DeleteDocumentAsync, ReadTextAsync, WriteTextAsync     |
| AddLinkAsync, UpdateLinkAsync, DeleteLinkAsync           |
+----------------------------------------------------------+
                ^
                :
+----------------------------------------------------------+        +---------------------------------------+
| WorkspaceRepository                                      |<-------| StorageChanges                        |
|----------------------------------------------------------|  each  |---------------------------------------|
| projects, folders, documents, links : dictionaries       |  write |  ProjectId, ReplacesProject           |
|   (in memory, filled from IStorageContext.Changed)       |  or    |  Projects, Folders, Documents, Links  |
+----------------------------------------------------------+  read  |  Removed...Ids, MovedIds              |
                |                                                   +---------------------------------------+
                v
+----------------------------------------------------------+        +---------------------------------------+
| <<interface>> IStorageContext   --|> IProjectLock        |        | <<interface>> IProjectLock            |
|----------------------------------------------------------|        |---------------------------------------|
| Changed : event StorageChanges                           |        | RunLockedAsync(projectId, work)       |
| LoadAllProjectsAsync, LoadNewProjectsAsync,              |        +---------------------------------------+
|   ReloadProjectAsync                                     |
| the same writes as the repository; each takes the record |
|   and gives a StorageChanges. Moves take the record,     |
|   the target, the index and the ETag.                    |
| ReadDocumentTextAsync(document)                          |
+----------------------------------------------------------+
                ^
                :
+----------------------------------------------------------+
| DiskStorageContext                                       |
|   projects as folders; per open project: ID, name, lock  |
+----------------------------------------------------------+
      |                 |                    |                       |
      v                 v                    v                       v
+---------------+  +--------------------+  +-------------------+  +------------------------------+
| SettingsFiles |  | ManifestTransaction|  | FileManager       |  | IProjectWatcher              |
| Formats/*     |  | journaled batch    |  | the only class    |  |   FileProjectWatcher         |
+---------------+  | write, recovery    |  | that touches the  |  |   skips paths in OwnWrites   |
                   +--------------------+  | project's files   |  +------------------------------+
                                           +-------------------+
                                                     |
                                                     v
                                           +-------------------+
                                           | OwnWrites         |
                                           +-------------------+

Settings files (JSON, in .odysseum/ folders):

  project.json   ProjectFile : FolderFile    the ID and settings of the project, and the top folder's layout
  folder.json    FolderFile                  ID, PinnedView, Views, ItemOrder, Documents : Dict<id, DocumentEntry>
  folders.json   PlacesFile                  path of each folder by ID
  documents.json PlacesFile                  path of each document by ID
  links.json     LinksFile                   Links : LinkEntry[]

Other storage: IProjectHistory (GitProjectHistory), ITemplateRepository (TemplateRepository),
IThemeRepository (ThemeRepository).
```

## 5. Plugins, views and templates

The core knows one view, `write`. Other views come from plugins. `Odysseum.Plugins.Views` adds `board`, `outline`
and `grid`; it loads through the same path as a third-party plugin, and the server does not reference it.

A plugin is a folder in the plugins folder with a `plugin.json` manifest, its assembly, and its client files in
`wwwroot`:

```
{ "id": "views", "name": "Default views", "version": "1.0.0", "assembly": "Odysseum.Plugins.Views.dll" }
```

Only the C# code registers views. Each `AddView` gives a name, a label, a client entry and an icon in `wwwroot`. The
web UI reads them from `GET /api/plugins`, shows the label and icon in the view selector, and imports the client entry
when the view is first selected. The client entry's default export is the view's Vue component; it registers nothing.

```
Abstractions (the plugin contract):

+----------------------------------+   +----------------------------------+   +----------------------------------+
| <<interface>> IPlugin            |   | <<interface>> IPluginRegistry    |   | <<interface>> IViewDefinition    |
|----------------------------------|   |----------------------------------|   |----------------------------------|
| Register(registry)               |   | AddView(view)                    |   | Name, Label : string             |
+----------------------------------+   | AddView(name, label, clientEntry,|   | ClientEntry?, Icon? : string     |
                ^                      |   icon?, folderSettings?)        |   | FolderSettings : string[]        |
                :                      +----------------------------------+   +----------------------------------+
                :                                      ^                                     ^
                :                                      :                                     :
+----------------------------------+   +----------------------------------+  +----------------------------------+
| ViewsPlugin   (plugins/          |   | <<internal>> PluginRegistry      |  | <<record>> ViewDefinition        |
|   Odysseum.Plugins.Views)        |   |   one per plugin; collects views |  +----------------------------------+
| board, outline, grid             |   +----------------------------------+
|   (board.js, outline.js, grid.js;|
|    grid: columnFolder)           |
+----------------------------------+

Server:

+----------------------------------------------+      +----------------------------------------------+
| PluginLoader               ..|> IPluginLoader|----->| PluginManifest            plugin.json        |
|----------------------------------------------|      |----------------------------------------------|
| Load(folder, disabledPlugins)                |      | Id, Name, Version, Assembly                  |
|   : InstalledPlugin[]                        |      | Read(folder)   (checks it; else the folder   |
|   reads each manifest, then loads the        |      |                 is skipped)                  |
|   enabled plugins; checks each view's files  |      +----------------------------------------------+
+----------------------------------------------+      +----------------------------------------------+
                      |                          ---->| <<internal>> PluginLoadContext               |
                      |                               |   one AssemblyLoadContext per plugin;        |
                      v                               |   Odysseum.Abstractions comes from the server|
+----------------------------------------------+      +----------------------------------------------+
| PluginRepository       ..|> IPluginRepository|      +----------------------------------------------+
|----------------------------------------------|      | <<record>> InstalledPlugin                   |
| GetAll() : InstalledPlugin[]                 |----->|----------------------------------------------|
|   (also disabled and failed plugins)         |      | Manifest, Folder, Status, Error?             |
+----------------------------------------------+      | Views : IViewDefinition[]  (empty when off)  |
    used by PluginsController (GET /api/plugins)      | Id, WwwRoot, ClientUrl(path)                 |
    and PluginHosting (/plugins/{id}/)                +----------------------------------------------+
                                                        Status: Enabled, Disabled or Failed (PluginStatus).
                                                        Error is set only for Failed.

+----------------------------------------------+      +----------------------------------------------+
| ViewCatalog                                  |      | <<static>> ViewNames                         |
|----------------------------------------------|      |----------------------------------------------|
| WriteView = "write"           (const)        |      | IsValid(name), Check(name)                   |
| Views : IViewDefinition[]  (write + plugins) |      | Removes(settings)  (JSON null)               |
|----------------------------------------------|      | CheckSettings(views)   (objects, <= 64 KB)   |
| CheckFolders(folderExists, views)            |      +----------------------------------------------+
| WithoutFolder(views, folderId) : changes     |
| MapFolders(views, map) : views               |
+----------------------------------------------+
  used by ProjectService, ProjectTemplates. A second view with the same name is logged and skipped.

+----------------------------------------------+      +----------------------------------------------+
| ProjectTemplate                              |      | <<interface>> ITemplateRepository            |
|----------------------------------------------|      |----------------------------------------------|
| Name : string                                |      | Root : string                                |
| Settings : TemplateSettings                  |      | EnsureDefault(), List(), Get(name),          |
| Folders : TemplateFolder[]                   |      | Save(template), Delete(name)                 |
| Documents : TemplateDocument[]               |      +----------------------------------------------+
+----------------------------------------------+
  TemplateFolder: Path, PinnedView, Children (names), Views (folder settings hold paths)
  TemplateDocument: Path, Title, Content?
```

## 6. API edge

Each resource has one controller. The project, folder, document, link and version controllers use only the service
interfaces.

```
+-----------------------+
| ProjectsController    |--> IProjectService
| FoldersController     |--> IProjectService
| DocumentsController   |--> IProjectService
| LinksController       |--> IProjectService
| VersionsController    |--> IHistoryService, IProjectService
| TemplatesController   |--> ITemplateRepository, IProjectService
| PluginsController     |--> IPluginRepository
| ThemesController      |--> IThemeRepository
| ServerSettings-,      |--> ISettingsProvider
| SessionController     |
+-----------------------+

+-----------------------+        +----------------------------------------------------------+
| ProjectHub  /api/hub  |        | ChangeEmitter                                            |
|-----------------------|        |----------------------------------------------------------|
| OpenProject(id)       |<-------| listens to IProjectService.Changed; for each batch sends |
| CloseProject(id)      |        | "changed" { projectId, changes: [type, kind, id, etag] } |
+-----------------------+        |   project changes   -> all browsers                      |
                                 |   other changes     -> the project's group               |
                                 +----------------------------------------------------------+
```
