import type { DocumentContent, DocumentSummary, MetadataFields, Project, ProjectInfo, ProjectSettings, Snapshot } from '../models'

/** The server's copy of a project as of the last successful sync. */
export interface MirroredProject { slug: string; project: Project; syncedAt: string }

/** The server's copy of a document; `document.revision` is the disk fingerprint, '' until a locally created file exists on the server. */
export interface MirroredDocument { slug: string; id: string; document: DocumentSummary; content: string }

/** A local text edit not yet accepted by the server. `baseRevision` is the fingerprint it was written against. */
export interface PendingEdit {
  slug: string
  id: string
  content: string
  baseRevision: string
  updated: string
  conflict: DocumentContent | 'deleted' | null
}

/** Every change other than typing, recorded in the order it was made and replayed in that order. */
export type LocalOp =
  | { type: 'createProject'; title: string; settings: ProjectSettings }
  | { type: 'create'; id: string; title: string; folder: string; path: string; content: string }
  | { type: 'metadata'; id: string; fields: MetadataFields; base: MetadataFields }
  | { type: 'move'; id: string; path: string }
  | { type: 'order'; ids: string[] }
  | { type: 'settings'; settings: ProjectSettings }
export interface PendingOp { seq?: number; slug: string; op: LocalOp; updated: string }

/** Version history fetched while online, kept so it can be read offline. */
export interface CachedSnapshots { slug: string; id: string; list: Snapshot[]; contents: Record<string, string> }

/**
 * Where the browser keeps its copy of the manuscript. IndexedDB today; a desktop build can
 * implement this over real files without touching the sync engine or the UI.
 */
export interface IMirrorStore {
  /** False when only in-memory storage was available (private browsing, storage blocked). */
  readonly durable: boolean

  listProjects(): Promise<ProjectInfo[]>
  putProjects(projects: ProjectInfo[]): Promise<void>
  getProject(slug: string): Promise<MirroredProject | undefined>
  putProject(project: MirroredProject): Promise<void>
  /** Re-keys everything stored under a project when the server assigns a different folder name. */
  renameProject(from: string, to: string): Promise<void>

  listDocuments(slug: string): Promise<MirroredDocument[]>
  getDocument(slug: string, id: string): Promise<MirroredDocument | undefined>
  putDocuments(documents: MirroredDocument[]): Promise<void>
  deleteDocument(slug: string, id: string): Promise<void>
  /** Re-keys a document, its pending edit, and every queued change that names it, when the server assigns the real id. */
  renameDocument(slug: string, from: string, to: string): Promise<void>

  listPending(slug: string): Promise<PendingEdit[]>
  getPending(slug: string, id: string): Promise<PendingEdit | undefined>
  putPending(edit: PendingEdit): Promise<void>
  deletePending(slug: string, id: string): Promise<void>

  listOps(slug: string): Promise<PendingOp[]>
  putOp(op: PendingOp): Promise<PendingOp>
  deleteOp(seq: number): Promise<void>

  getSnapshots(slug: string, id: string): Promise<CachedSnapshots | undefined>
  putSnapshots(snapshots: CachedSnapshots): Promise<void>
}

/** Rewrites document ids inside a queued change; shared by every store implementation. */
export function renameInOp(op: LocalOp, from: string, to: string): LocalOp {
  switch (op.type) {
    case 'create': case 'move': return op.id === from ? { ...op, id: to } : op
    case 'metadata': {
      // The scene itself, and any character or location attached to it, may have been created offline.
      const swap = (ids: string[]) => ids.map(id => id === from ? to : id)
      const positions = (values: Record<string, number> = {}) => Object.fromEntries(Object.entries(values).map(([id, position]) => [id === from ? to : id, position]))
      const renamed = { ...op, fields: { ...op.fields, characters: swap(op.fields.characters ?? []), locations: swap(op.fields.locations ?? []), arcPositions: positions(op.fields.arcPositions) }, base: { ...op.base, characters: swap(op.base.characters ?? []), locations: swap(op.base.locations ?? []), arcPositions: positions(op.base.arcPositions) } }
      return op.id === from ? { ...renamed, id: to } : renamed
    }
    case 'order': return { ...op, ids: op.ids.map(id => id === from ? to : id) }
    default: return op
  }
}
