import { countWords, type DocumentSummary, type Project, type ProjectSettings } from '../models'
import { kindFor } from '../services/FileNames'
import type { LocalOp, MirroredDocument, PendingOp } from '../storage'
import type { SyncContext } from './SyncContext'

/** The server's copy with every queued local change applied, in the order it was made: what the writer should see. */
export function overlay(server: Project, ops: PendingOp[], documents: Map<string, MirroredDocument>): Project {
  let settings: ProjectSettings = server.settings
  let list: DocumentSummary[] = server.documents.map(doc => ({ ...doc, characters: doc.characters ?? [], locations: doc.locations ?? [], arcPositions: { ...(doc.arcPositions ?? {}) } }))
  for (const { op } of ops) {
    switch (op.type) {
      case 'create':
        if (!list.some(doc => doc.id === op.id)) list.push(documents.get(op.id)?.document ?? summaryFor(op, list, settings))
        break
      case 'metadata': {
        const doc = list.find(item => item.id === op.id)
        if (doc) Object.assign(doc, op.fields)
        break
      }
      case 'move': {
        const doc = list.find(item => item.id === op.id)
        if (doc) { doc.kind = kindFor(op.path); doc.path = op.path; doc.folder = op.path.split('/').slice(0, -1).join('/') }
        break
      }
      case 'order': {
        const position = new Map(op.ids.map((id, index) => [id, index]))
        const known = list.filter(doc => position.has(doc.id)).sort((a, b) => position.get(a.id)! - position.get(b.id)!)
        list = [...known, ...list.filter(doc => !position.has(doc.id))].map((doc, index) => ({ ...doc, order: index }))
        break
      }
      case 'settings':
      case 'createProject':
        settings = op.settings
        break
    }
  }
  return { ...server, settings, documents: list }
}

/** The summary a scene created offline shows until the server has it. */
export function summaryFor(op: Extract<LocalOp, { type: 'create' }>, existing: DocumentSummary[], settings: ProjectSettings): DocumentSummary {
  return {
    id: op.id, path: op.path, title: op.title, folder: op.folder, synopsis: '', notes: '', status: 'draft',
    wordGoal: settings.defaultSceneWordGoal, order: existing.length ? Math.max(...existing.map(doc => doc.order)) + 1 : 0,
    wordCount: countWords(op.content), revision: '', lastModified: new Date().toISOString(),
    kind: kindFor(op.path), characters: [], locations: [], arcPositions: {},
  }
}

/** Rebuilds the view from the mirror and hands it to the listener. */
export async function publishView(context: SyncContext): Promise<Project | undefined> {
  const mirrored = await context.mirror.getProject(context.slug)
  if (!mirrored) return undefined
  const [ops, documents] = await Promise.all([context.mirror.listOps(context.slug), context.mirror.listDocuments(context.slug)])
  const view = overlay(mirrored.project, ops, new Map(documents.map(doc => [doc.id, doc])))
  context.listener.onProject(view)
  return view
}

export async function countPending(context: SyncContext) {
  const [edits, ops] = await Promise.all([context.mirror.listPending(context.slug), context.mirror.listOps(context.slug)])
  return edits.length + ops.length
}
