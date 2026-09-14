import type { DocumentSummary, MetadataFields, ProjectSettings } from '../models'
import { pathFor } from '../services/FileNames'
import type { LocalOp, PendingOp } from '../storage'
import type { ILocalChanges } from './ILocalChanges'
import { publishView, summaryFor } from './ProjectView'
import type { SyncContext } from './SyncContext'

export class LocalChanges implements ILocalChanges {
  constructor(private readonly context: SyncContext, private readonly requestSync: () => void) {}

  async createDocument(title: string, folder: string, content = ''): Promise<DocumentSummary> {
    const { mirror, listener } = this.context
    const slug = this.context.slug
    const view = await publishView(this.context)
    const taken = new Set((view?.documents ?? []).map(doc => doc.path.toLowerCase()))
    const op: Extract<LocalOp, { type: 'create' }> = {
      type: 'create', id: `local-${crypto.randomUUID()}`, title: title.trim(), folder: folder.trim().replace(/^\/+|\/+$/g, ''),
      path: pathFor(title, folder, taken), content,
    }
    const document = summaryFor(op, view?.documents ?? [], view?.settings ?? { title: '', wordGoal: 0, defaultSceneWordGoal: 1000 })
    const mirrored = { slug, id: op.id, document, content }
    await mirror.putDocuments([mirrored])
    await this.record(op)
    listener.onDocument(mirrored, undefined)
    return document
  }

  async updateMetadata(id: string, fields: MetadataFields, base: MetadataFields) {
    const existing = await this.find(op => op.type === 'metadata' && op.id === id)
    // Later edits to the same scene replace the earlier queued ones but keep the original base for the server-side check.
    const original = existing?.op.type === 'metadata' ? existing.op.base : base
    await this.record({ type: 'metadata', id, fields, base: original }, existing)
  }

  async moveDocument(id: string, path: string) {
    await this.record({ type: 'move', id, path }, await this.find(op => op.type === 'move' && op.id === id))
  }

  async reorder(ids: string[]) {
    await this.record({ type: 'order', ids }, await this.find(op => op.type === 'order'))
  }

  async updateSettings(settings: ProjectSettings) {
    const creating = await this.find(op => op.type === 'createProject')
    if (creating && creating.op.type === 'createProject') {
      await this.record({ ...creating.op, title: settings.title, settings }, creating)
      return
    }
    await this.record({ type: 'settings', settings }, await this.find(op => op.type === 'settings'))
  }

  private async find(match: (op: LocalOp) => boolean) {
    return (await this.context.mirror.listOps(this.context.slug)).find(pending => match(pending.op))
  }

  private async record(op: LocalOp, replace?: PendingOp) {
    // An auto-increment key must be absent, not undefined, for IndexedDB to assign one.
    const pending: PendingOp = { slug: this.context.slug, op, updated: new Date().toISOString() }
    if (replace?.seq !== undefined) pending.seq = replace.seq
    await this.context.mirror.putOp(pending)
    await publishView(this.context)
    this.requestSync()
  }
}
