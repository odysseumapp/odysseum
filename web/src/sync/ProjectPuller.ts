import { ApiError } from '../api/IApiClient'
import type { DocumentContent } from '../models'
import type { MirroredDocument, PendingEdit } from '../storage'
import { publishView } from './ProjectView'
import type { SyncContext } from './SyncContext'

/** Brings the server's changes into the mirror and marks conflicts against pending edits. Nothing is merged. */
export class ProjectPuller {
  constructor(private readonly context: SyncContext) {}

  /** Returns false when the project does not exist on the server yet (created offline, not replayed). */
  async pull(): Promise<boolean> {
    const { api, mirror, listener } = this.context
    const slug = this.context.slug
    if ((await mirror.listOps(slug)).some(pending => pending.op.type === 'createProject')) return false
    const project = await api.getProject(slug)
    await mirror.putProject({ slug, project, syncedAt: new Date().toISOString() })
    const mirrored = new Map((await mirror.listDocuments(slug)).map(doc => [doc.id, doc]))
    const pendings = new Map((await mirror.listPending(slug)).map(edit => [edit.id, edit]))
    const changed = project.documents.filter(summary => mirrored.get(summary.id)?.document.revision !== summary.revision)
    const fresh = new Map<string, DocumentContent>()
    if (changed.length > 5 || mirrored.size === 0) {
      // A cold start or a large external change: one request for the whole manuscript.
      for (const item of await api.listDocuments(slug)) fresh.set(item.document.id, item)
    } else {
      for (const summary of changed) {
        try { fresh.set(summary.id, await api.getDocument(slug, summary.id)) }
        catch (ex) { if (!(ex instanceof ApiError && ex.status === 404)) throw ex }
      }
    }
    const updates: MirroredDocument[] = []
    for (const summary of project.documents) {
      const content = fresh.get(summary.id)
      const existing = mirrored.get(summary.id)
      if (content) updates.push({ slug, id: summary.id, document: content.document, content: content.content })
      else if (existing) updates.push({ ...existing, document: summary })
    }
    await mirror.putDocuments(updates)
    const view = await publishView(this.context)
    const summaries = new Map(view?.documents.map(doc => [doc.id, doc]))
    for (const stored of updates) {
      // Prose comes from the mirror; displayed details include queued local changes.
      const doc = { ...stored, document: summaries.get(stored.id) ?? stored.document }
      const pending = pendings.get(doc.id)
      const contentChanged = fresh.has(doc.id) && mirrored.get(doc.id)?.content !== doc.content
      if (pending && contentChanged) {
        if (pending.content === doc.content) {
          await mirror.deletePending(slug, doc.id)
          listener.onDocument(doc, undefined)
          continue
        }
        if (pending.baseRevision !== doc.document.revision) {
          pending.conflict = { document: doc.document, content: doc.content }
          await mirror.putPending(pending)
        }
      }
      listener.onDocument(doc, pending)
    }
    for (const [id, existing] of mirrored) {
      if (project.documents.some(summary => summary.id === id)) continue
      if (existing.document.revision === '') continue // Created here, not on the server yet.
      const pending = pendings.get(id)
      if (pending) {
        pending.conflict = 'deleted'
        await mirror.putPending(pending)
        listener.onDocument(existing, pending)
      } else if (listener.isOpen(id)) {
        // Keep the text the writer is looking at as a draft rather than pulling it out from under them.
        const kept: PendingEdit = { slug, id, content: existing.content, baseRevision: existing.document.revision, updated: new Date().toISOString(), conflict: 'deleted' }
        await mirror.putPending(kept)
        listener.onDocument(existing, kept)
      } else {
        await mirror.deleteDocument(slug, id)
        listener.onRemoved(id)
      }
    }
    return true
  }
}
