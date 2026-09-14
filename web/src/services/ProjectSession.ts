import type { IOdysseumApi } from '../api/IOdysseumApi'
import type { IMirrorStore, MirroredDocument, PendingEdit } from '../storage'
import type { ILocalChanges } from '../sync/ILocalChanges'
import type { ISyncEngine, ISyncListener } from '../sync/ISyncEngine'
import { LocalChanges } from '../sync/LocalChanges'
import { publishView } from '../sync/ProjectView'
import type { SyncContext } from '../sync/SyncContext'
import { SyncEngine } from '../sync/SyncEngine'
import { HistoryCache } from './HistoryCache'

/** One open project: its local copy, the engine that keeps it in step with the server, and the ways to change it. */
export class ProjectSession {
  readonly engine: ISyncEngine
  readonly changes: ILocalChanges
  readonly history: HistoryCache
  private readonly context: SyncContext

  constructor(api: IOdysseumApi, mirror: IMirrorStore, slug: string, listener: ISyncListener) {
    this.context = { api, mirror, slug, listener }
    this.engine = new SyncEngine(this.context)
    this.changes = new LocalChanges(this.context, () => this.engine.syncSoon())
    this.history = new HistoryCache(api, mirror)
  }

  get slug() { return this.context.slug }

  /** Shows whatever this device already has. Returns false when the project has never been opened here. */
  async hydrate(): Promise<boolean> {
    const { mirror, listener } = this.context
    const view = await publishView(this.context)
    if (!view) return false
    const summaries = new Map(view.documents.map(doc => [doc.id, doc]))
    const pendings = new Map<string, PendingEdit>((await mirror.listPending(this.slug)).map(edit => [edit.id, edit]))
    for (const doc of await mirror.listDocuments(this.slug) as MirroredDocument[])
      listener.onDocument({ ...doc, document: summaries.get(doc.id) ?? doc.document }, pendings.get(doc.id))
    return true
  }

  start() { this.engine.start() }

  async close() {
    await this.engine.flush()
    this.engine.stop()
  }
}
