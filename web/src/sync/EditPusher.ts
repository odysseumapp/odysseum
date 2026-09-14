import { ApiError } from '../api/IApiClient'
import type { DocumentContent } from '../models'
import type { MirroredDocument } from '../storage'
import type { SyncContext } from './SyncContext'

/** Sends pending text edits with the fingerprint each was written against; a refused edit becomes a conflict. */
export class EditPusher {
  constructor(private readonly context: SyncContext) {}

  async push(): Promise<number> {
    const { api, mirror, listener } = this.context
    const slug = this.context.slug
    let pushed = 0
    for (const edit of await mirror.listPending(slug)) {
      if (edit.conflict || edit.baseRevision === '') continue // Conflicts wait for the writer; '' waits for the file to be created.
      try {
        const saved = await api.saveDocument(slug, edit.id, edit.content, edit.baseRevision)
        const doc: MirroredDocument = { slug, id: edit.id, document: saved.document, content: saved.content }
        await mirror.putDocuments([doc])
        const latest = await mirror.getPending(slug, edit.id)
        if (latest && latest.content !== saved.content) {
          // Typed more while the save was in flight: the newer text is now based on what was just saved.
          latest.baseRevision = saved.document.revision
          await mirror.putPending(latest)
          listener.onDocument(doc, latest)
        } else {
          await mirror.deletePending(slug, edit.id)
          listener.onDocument(doc, undefined)
        }
        pushed++
      } catch (ex) {
        if (!(ex instanceof ApiError && (ex.status === 409 || ex.status === 404))) throw ex
        await this.markConflict(edit.id)
      }
    }
    return pushed
  }

  private async markConflict(id: string) {
    const { api, mirror, listener } = this.context
    const slug = this.context.slug
    let remote: DocumentContent | 'deleted'
    try { remote = await api.getDocument(slug, id) }
    catch (inner) {
      if (inner instanceof ApiError && inner.status === 404) remote = 'deleted'
      else throw inner
    }
    const latest = await mirror.getPending(slug, id)
    if (!latest) return
    if (remote === 'deleted') {
      latest.conflict = 'deleted'
      await mirror.putPending(latest)
      const existing = await mirror.getDocument(slug, id)
      if (existing) listener.onDocument(existing, latest)
      return
    }
    const doc: MirroredDocument = { slug, id, document: remote.document, content: remote.content }
    await mirror.putDocuments([doc])
    if (remote.content === latest.content) {
      await mirror.deletePending(slug, id)
      listener.onDocument(doc, undefined)
      return
    }
    latest.conflict = remote
    await mirror.putPending(latest)
    listener.onDocument(doc, latest)
  }
}
