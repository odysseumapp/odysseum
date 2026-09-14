# Server services

Names describe responsibilities. `Store` reads and writes persistent data, `Codec` handles a document format,
`Monitor` watches for changes, `Events` distributes notifications, and `Factory` constructs a running project.
Use a `Service` suffix when it clarifies an application workflow; it is not required for classes in `Services/`.

| Component | Responsibility |
| --- | --- |
| `ProjectLibrary` | Lists and creates project folders; caches one open handle per project and owns its lifetime. |
| `ProjectFactory` | Constructs the project services, events, settings provider, and monitor; cleans up failed opens. |
| `ProjectHandle` | Groups the running project components and stops monitoring before disposing the services. |
| `ProjectServices` | Public entry point; serializes operations and coordinates refreshes, writes, and responses. |
| `Projects/ProjectState` | Holds committed metadata, observed documents, and revision; publishes successful state changes. |
| `Projects/ProjectScanner` | Discovers files and reconciles document identities and metadata. |
| `Projects/ProjectDocumentService` | Creates, saves, and moves documents with revision checks and recovery snapshots. |
| `Projects/ProjectOrganizationService` | Validates and updates scene details, project settings, and manuscript order. |
| `Projects/ProjectQueries` | Builds detached responses, search results, and manuscript exports from current state. |
| `Storage/ProjectFileStore` | Validates paths, excludes links, bounds reads, replaces files atomically, and acquires the instance lock. |
| `Storage/ProjectManifestStore` | Reads and migrates root and folder manifests, assembles the flat project view, and checks a combined revision. |
| `Storage/ManifestTransaction` | Journals multi-manifest writes and restores incomplete batches under the project lock. |
| `Storage/DocumentHistoryStore` | Writes and reads Markdown recovery snapshots, deduplicated by hash. |
| `Storage/Models/` | Represents persisted metadata and observed documents. |
| `Documents/MarkdownDocumentCodec` | Encodes UTF-8, separates preserved frontmatter/BOM from prose, and counts words. |
| `Documents/DocumentRules` | Defines supported document extensions, folder classification, and naming rules. |
| `Monitoring/ProjectMonitor` | Requests scans after filesystem notifications and on a timer. |
| `Monitoring/ProjectEvents` | Publishes project invalidations to subscribers; the API controller supplies SSE transport. |
| `Bootstrap/DemoContent` | Seeds the sample project when enabled and the workspace is empty. |

`ProjectServices` owns one semaphore for the project's operations. Its focused collaborators are internal,
share one `ProjectState`, and run under that semaphore. Controllers and monitors call the coordinator;
they cannot bypass it to call the collaborators directly. Storage components do not keep separate copies
of project state or acquire independent operation locks.
The per-project instance file lock continues to prevent another server process from opening the project.

Metadata changes and scans work on a candidate manifest. `ProjectState` replaces its current manifest and
revision only after persistence succeeds. Rejected requests, failed replacements, and incomplete scans
therefore cannot leave changes that a later operation accidentally saves. Returned document summaries
also copy character and location lists and arc position maps so callers cannot mutate stored metadata through a response.

The coordinator refreshes state before queries and writes. Document writes are followed by a scan before
returning a response; organization changes already have their new manifest and need no second scan.
History reads use the same lock and the last observed document identities, as before this separation.

Project listing uses the same manifest reader as an open project. It does not persist migrations or start
monitors; unreadable or invalid metadata falls back to the folder name until opening reports the error.
Settings updates return the project response produced by the write, avoiding a second scan in the controller.

HTTP routes, JSON response envelopes, document IDs, and the SSE `workspace` event name remain compatible
with existing clients. The root manifest migrates to version 2 with metadata owned by each content folder;
see [the project format](project-format.md). Locations and arcs add document kinds and optional metadata request fields. Markdown and manifest replacement are still separate
filesystem operations, and arbitrary external editors do not participate in the project's semaphore.

The Arcs view renders one horizontal timeline per arc document. `arcPositions` belongs to the attached
document, so each arc can arrange its points independently without changing manuscript order.
The browser queues these as ordinary metadata operations and remaps arc IDs during offline replay.
