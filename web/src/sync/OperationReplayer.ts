import { ApiError, isOffline } from '../api/IApiClient'
import type { DocumentSummary, MetadataFields } from '../models'
import type { LocalOp, PendingEdit, PendingOp } from '../storage'
import type { SyncContext } from './SyncContext'

const fields = (doc: DocumentSummary | MetadataFields): MetadataFields =>
  ({ title: doc.title, synopsis: doc.synopsis, notes: doc.notes, status: doc.status, wordGoal: doc.wordGoal, characters: [...(doc.characters ?? [])], locations: [...(doc.locations ?? [])], arcPositions: Object.fromEntries(Object.entries(doc.arcPositions ?? {}).sort(([a], [b]) => a.localeCompare(b))) })
const same = (a: MetadataFields, b: MetadataFields) => JSON.stringify(fields(a)) === JSON.stringify(fields(b))

/**
 * Replays queued operations against the server in the order they were made. Each is applied against the
 * server's current state (a stale revision is retried once); one the server refuses for good is dropped
 * and reported, with the writer's text kept reachable.
 */
export class OperationReplayer {
  constructor(private readonly context: SyncContext) {}

  async replay(): Promise<number> {
    let replayed = 0
    const attempts = new Map<number, number>()
    // Always take the current head of the queue: applying one operation can rewrite the ids in those behind it.
    while (true) {
      const [pending] = await this.context.mirror.listOps(this.context.slug)
      if (!pending) break
      try {
        await this.apply(pending.op)
        await this.context.mirror.deleteOp(pending.seq!)
        replayed++
      } catch (ex) {
        if (isOffline(ex)) throw ex
        const tries = (attempts.get(pending.seq!) ?? 0) + 1
        attempts.set(pending.seq!, tries)
        if (ex instanceof ApiError && ex.status === 409 && tries < 2) continue
        await this.giveUp(pending, ex)
      }
    }
    return replayed
  }

  private async apply(op: LocalOp) {
    const { api, mirror, listener } = this.context
    switch (op.type) {
      case 'createProject': {
        const created = await api.createProject(op.title, op.settings.wordGoal)
        if (created.slug !== this.context.slug) {
          await mirror.renameProject(this.context.slug, created.slug)
          this.context.slug = created.slug
          listener.onProjectRenamed(created.slug)
        }
        const project = await api.getProject(this.context.slug)
        if (!same(project.settings as never, op.settings as never) || project.settings.defaultSceneWordGoal !== op.settings.defaultSceneWordGoal)
          await api.updateSettings(this.context.slug, op.settings, project.revision)
        break
      }
      case 'create': {
        const slug = this.context.slug
        const created = await api.createDocument(slug, op.title, op.folder, op.content)
        const real = created.document.id
        if (real !== op.id) {
          await mirror.renameDocument(slug, op.id, real)
          listener.onDocumentRenamed(op.id, real)
        }
        const doc = { slug, id: real, document: created.document, content: created.content }
        await mirror.putDocuments([doc])
        const edit = await mirror.getPending(slug, real)
        if (edit && edit.content === created.content) await mirror.deletePending(slug, real)
        else if (edit) { edit.baseRevision = created.document.revision; await mirror.putPending(edit) }
        listener.onDocument(doc, edit && edit.content !== created.content ? edit : undefined)
        break
      }
      case 'metadata': {
        const slug = this.context.slug
        const project = await api.getProject(slug)
        const current = project.documents.find(doc => doc.id === op.id)
        if (!current) throw new ApiError(404, `"${op.fields.title}" no longer exists on the server, so its details were not saved.`)
        if (!same(current, op.base) && !same(current, op.fields)) {
          listener.onMetadataRejected(op.id, op.fields, `Details for "${current.title}" changed elsewhere. Your version is back in the inspector to review.`)
          break
        }
        await api.updateMetadata(slug, op.id, op.fields, project.revision)
        break
      }
      case 'move': {
        const slug = this.context.slug
        const current = await api.getDocument(slug, op.id)
        if (current.document.path === op.path) break
        const moved = await api.moveDocument(slug, op.id, op.path, current.document.revision)
        await mirror.putDocuments([{ slug, id: op.id, document: moved.document, content: moved.content }])
        break
      }
      case 'order': {
        const slug = this.context.slug
        const project = await api.getProject(slug)
        const known = new Set(project.documents.map(doc => doc.id))
        const ids = [...op.ids.filter(id => known.has(id)), ...project.documents.filter(doc => !op.ids.includes(doc.id)).map(doc => doc.id)]
        await api.reorder(slug, ids, project.revision)
        break
      }
      case 'settings': {
        const slug = this.context.slug
        const project = await api.getProject(slug)
        await api.updateSettings(slug, op.settings, project.revision)
        break
      }
    }
  }

  private async giveUp(pending: PendingOp, ex: unknown) {
    const { mirror, listener } = this.context
    const slug = this.context.slug
    await mirror.deleteOp(pending.seq!)
    const message = ex instanceof Error ? ex.message : 'The server refused a change.'
    const op = pending.op
    if (op.type === 'create') {
      // The file could not be created; keep the text as a draft the writer can save elsewhere.
      const edit = await mirror.getPending(slug, op.id)
      const kept: PendingEdit = { slug, id: op.id, content: edit?.content ?? op.content, baseRevision: '', updated: new Date().toISOString(), conflict: 'deleted' }
      await mirror.putPending(kept)
      const doc = await mirror.getDocument(slug, op.id)
      if (doc) listener.onDocument(doc, kept)
      listener.onProblem(`"${op.title}" could not be created on the server: ${message}`)
    } else if (op.type === 'createProject') {
      listener.onProblem(`The project "${op.title}" could not be created on the server: ${message}`)
    } else {
      listener.onProblem(message)
    }
  }
}
