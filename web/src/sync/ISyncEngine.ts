import type { MetadataFields, Project } from '../models'
import type { MirroredDocument, PendingEdit } from '../storage'

export interface SyncStatus {
  /** The last pass reached the server. */
  online: boolean
  syncing: boolean
  /** Local changes the server has not accepted yet: edits, conflicts, and queued operations. */
  pending: number
  lastSync: string | null
  /** A failure that is not simply "offline"; shown to the writer. */
  error: string
}

/** How the sync layer reports what it did. The UI adapter implements this; nothing in sync/ knows Vue. */
export interface ISyncListener {
  /** The project as the writer should see it: the server's copy with every queued local change applied. */
  onProject(project: Project): void
  /** The mirror or the pending edit for this document changed. */
  onDocument(document: MirroredDocument, pending: PendingEdit | undefined): void
  /** The server assigned a real id to a document created offline. */
  onDocumentRenamed(from: string, to: string): void
  /** The document is gone from the server and nothing local is left to keep. */
  onRemoved(id: string): void
  /** The server assigned a different folder name to a project created offline. */
  onProjectRenamed(slug: string): void
  onStatus(status: SyncStatus): void
  /** A queued change the server refused for a reason other than being offline; the change was dropped. */
  onProblem(message: string): void
  /** Scene details refused because they changed elsewhere; the fields are handed back to the writer. */
  onMetadataRejected(id: string, fields: MetadataFields, message: string): void
  /** Whether the writer is looking at this document; a deleted open document is kept as a draft instead of vanishing. */
  isOpen(id: string): boolean
}

/** Keeps one project's local copy and the server in step. Timing and connectivity live here; the work is delegated. */
export interface ISyncEngine {
  readonly slug: string
  start(): void
  stop(): void
  /** Runs a pass now, or joins the one in flight and queues another after it. Never throws. */
  syncNow(): Promise<void>
  /** Runs a pass soon; repeated calls collapse into one. */
  syncSoon(delay?: number): void
  /** Records typing. Written to the mirror after a short pause so keystrokes stay cheap. */
  edit(id: string, content: string): void
  /** Writes every typed edit to the mirror immediately (before the page hides or unloads). */
  flush(): Promise<void>
  hasUnwritten(): boolean
  isWriting(id: string): boolean
  /** Conflict choices. */
  useServer(id: string): Promise<void>
  keepMine(id: string): Promise<void>
  discard(id: string): Promise<void>
}
