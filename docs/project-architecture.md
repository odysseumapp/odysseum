# Project architecture

This document shows the classes of the server and how they connect. It matches the code.

Words used here:

- A **project name** is the project's folder name in the workspace. Example: `my-novel`.
- A **project branch** is a project name with a branch name. The type is `ProjectBranch(Project, Branch)`. Only the branch `main` exists now.
- An **item** is a folder or a document.

## Part 1: Data in memory

```
                                  controllers (API)
                                          |
        +------------------+--------------+---+------------------+
        v                  v                  v                  v
+----------------+ +---------------+ +-----------------+ +----------------+
| ProjectService | | FolderService | | DocumentService | | HistoryService |
+----------------+ +---------------+ +-----------------+ +----------------+
        |                  |                  |                  |
        +------------------+--------------+---+------------------+
                                          | OpenAsync(branch)
                                          v
                           +------------------------------+
                           | ProjectSessions              |
                           |------------------------------|
                           | Get(branch)                  |
                           | OpenAsync(branch)            |
                           | CloseAsync(branch)           |
                           | ListAsync()                  |
                           | CreateAsync(title, ...)      |
                           +------------------------------+
                                          | 1..*
                                          v
                           +------------------------------+
                           | ProjectSession               |
                           |------------------------------|
                           | Branch : ProjectBranch       |
                           | Current : Project            |
                           | Events : ProjectEvents       |
                           | RunAsync(op)   one lock      |
                           | SaveAsync(changes, revision) |
                           | LoadBodyAsync(documentId)    |
                           +------------------------------+
                                          | holds
                                          v
+----------------------+   +------------------------------+
| FolderEditor         |   | Project            immutable |
| DocumentEditor       |   |------------------------------|
|----------------------|-->| Branch, Name, Id, Title      |
| edit a copy          |   | Settings, Revision, Warning  |
| mark changed items   |   | Root : Folder                |
| Result() : Project   |   | Folder(id), Document(id)     |
| Changes() : Changes  |   | FolderAt(path), PathOf(item) |
+----------------------+   | Walk() : documents in order  |
                           +------------------------------+
                                          | holds items
                                          v
                           +------------------------------+
                           | Item      abstract immutable |
                           |------------------------------|
                           | Id, Name                     |
                           | ParentId        read-only    |
                           | OrderInParent   read-only    |
                           | Path            read-only    |
                           +------------------------------+
                                          ^
                                          |
                         +----------------+----------------+
                         |                                 |
          +------------------------------+  +------------------------------+
          | Folder             immutable |  | Document           immutable |
          |------------------------------|  |------------------------------|
          | Children : Item[]  in order  |  | Title, Synopsis, Notes,      |
          | OwnDocument : Document?      |  |   Status, WordGoal           |
          | PinnedView, GridFolderId     |  | Links, LinkNotes             |
          +------------------------------+  | Kind, IsFolderDocument       |
                                            | Revision, Modified,          |
                                            |   WordCount                  |
                                            | Body : null until opened     |
                                            +------------------------------+
```

- `Project` sets `ParentId`, `OrderInParent` and `Path` when it makes each copy. No other class sets them.
- `Children` is the only source of the order. The folder's own hidden document is not in `Children`.
- To change the order, or to move an item to another folder, use `FolderService.MoveAsync(branch, itemId, targetFolderId, index, revision)`.
- Every service operation runs in `ProjectSession.RunAsync`. The session loads the project again before the operation, then the operation checks the revision, edits a copy with an editor, and gives the editor's `Changes` to `SaveAsync`.

## Part 2: Storage

`ProjectSessions` uses `IProjectRepository`, `IProjectWatcher` and `IProjectHistory`. `HistoryService` uses `IProjectHistory`. The services never see paths, streams or git types.

```
                 +------------------------------------+
                 | <<interface>>                      |
                 | IProjectRepository                 |
                 |------------------------------------|
                 | ListAsync()                        |
                 | ExistsAsync(branch)                |
                 | CreateAsync(title)                 |
                 | OpenAsync(branch) : lease          |
                 | LoadAsync(branch) : ProjectData    |
                 | LoadBodyAsync(branch, id)          |
                 | SaveAsync(branch, changes, rev)    |
                 +------------------------------------+
                                ^
                                |
               +----------------+----------------+
               |                                 |
+------------------------------+  +------------------------------+
| DiskProjectRepository        |  | (a database repository)      |
|------------------------------|  |------------------------------|
| files, JSON manifests        |  | not built yet                |
| scan on every load           |  | Branch column on each row    |
| lock file, save journal      |  | Revision = concurrency token |
+------------------------------+  +------------------------------+


                 +------------------------------------+
                 | <<interface>>                      |
                 | IProjectHistory                    |
                 |------------------------------------|
                 | SaveVersionAsync(branch, label?)   |
                 | ListVersionsAsync(branch, path?)   |
                 | RestoreAsync(branch, id, path?)    |
                 | ReadAsync(branch, id, path)        |
                 | CreateBranchAsync(branch, name)    |
                 | MergeAsync(from, to)               |
                 | Close(branch)                      |
                 +------------------------------------+
                                ^
                                |
               +----------------+----------------+
               |                                 |
+------------------------------+  +------------------------------+
| GitProjectHistory            |  | (a database history)         |
|------------------------------|  |------------------------------|
| git commits, one branch      |  | not built yet                |
| branch and merge: not        |  | version table: full copy     |
|   supported yet              |  | merge: compare by Id         |
+------------------------------+  +------------------------------+


                 +------------------------------------+
                 | <<interface>>                      |
                 | IProjectWatcher                    |
                 |------------------------------------|
                 | event Changed(branch)              |
                 | Watch(branch), Unwatch(branch)     |
                 | skips own writes                   |
                 +------------------------------------+
                                ^
                                |
               +----------------+----------------+
               |                                 |
+------------------------------+  +------------------------------+
| FileProjectWatcher           |  | (a database watcher)         |
|------------------------------|  |------------------------------|
| file system watcher          |  | not built yet                |
| asks OwnWrites               |  | reads Revision column        |
| poll every ScanSeconds       |  |   at intervals, or none      |
+------------------------------+  +------------------------------+
```

- `ProjectData` = settings + folder tree + document summaries + `Revision`. `Project` places the tree.
- `Changes` = settings? + changed folders + changed documents + removed folder IDs. Storage finds creations (unknown ID), moves (new path) and body writes (body not null) in it.
- The document text loads only with `LoadBodyAsync`.
- `OpenAsync` takes the instance lock and repairs an interrupted save. Dispose the result to release the lock.
- `ReadAsync` gives the text of one document in one old version. `Close` releases the git objects of a branch.
- `OwnWrites` records each path the server changes and the state it left it in. The watcher does not report an event for a path that is still in that state.

## Part 3: One save, step by step

```
controller
   | SaveBodyAsync(branch, id, body, revision)
   v
DocumentService
   | OpenAsync(branch)
   v
ProjectSessions ----> ProjectSession.RunAsync
                         | 1. RefreshAsync: LoadAsync(branch) -> Current
                         | 2. check revision against Current
                         | 3. editor = new DocumentEditor(Current); editor.SetBody(id, body)
                         | 4. SaveAsync(editor.Changes(), Current.Revision)
                         |       -> IProjectRepository.SaveAsync
                         |       -> Current = new Project(saved data)
                         |       -> Events.Publish(), Changed
                         v
                      returns the document from the new Current
```
