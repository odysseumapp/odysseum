import { isOffline } from '../api/IApiClient'
import { EditPusher } from './EditPusher'
import type { ISyncEngine, SyncStatus } from './ISyncEngine'
import { OperationReplayer } from './OperationReplayer'
import { ProjectPuller } from './ProjectPuller'
import { countPending, publishView } from './ProjectView'
import type { SyncContext } from './SyncContext'

const FULL_SYNC_MS = 20000
const PUSH_DELAY_MS = 850
const WRITE_DELAY_MS = 150
const MIN_BACKOFF_MS = 2000
const MAX_BACKOFF_MS = 30000

/**
 * Owns timing and connectivity for one project: when to run a pass, how to back off while the server is
 * unreachable, and how typing reaches the mirror. A pass is: replay queued operations, pull, push edits.
 */
export class SyncEngine implements ISyncEngine {
  private readonly replayer: OperationReplayer
  private readonly puller: ProjectPuller
  private readonly pusher: EditPusher
  private status: SyncStatus = { online: true, syncing: false, pending: 0, lastSync: null, error: '' }
  private timer: ReturnType<typeof setTimeout> | undefined
  private pushTimer: ReturnType<typeof setTimeout> | undefined
  private writes = new Map<string, { content: string; timer: ReturnType<typeof setTimeout> }>()
  private events: EventSource | undefined
  private channel: BroadcastChannel | undefined
  private passing = false
  private again = false
  private current: Promise<void> = Promise.resolve()
  private backoff = MIN_BACKOFF_MS
  private stopped = true

  constructor(private readonly context: SyncContext) {
    this.replayer = new OperationReplayer(context)
    this.puller = new ProjectPuller(context)
    this.pusher = new EditPusher(context)
  }

  get slug() { return this.context.slug }

  start() {
    this.stopped = false
    this.connectEvents()
    if ('BroadcastChannel' in globalThis) {
      this.channel = new BroadcastChannel('odysseum-sync')
      this.channel.onmessage = event => { if (event.data?.slug === this.slug) void this.syncNow() }
    }
    window.addEventListener('online', this.onOnline)
    document.addEventListener('visibilitychange', this.onVisible)
    void this.syncNow()
  }

  stop() {
    this.stopped = true
    this.events?.close()
    this.channel?.close()
    window.removeEventListener('online', this.onOnline)
    document.removeEventListener('visibilitychange', this.onVisible)
    clearTimeout(this.timer)
    clearTimeout(this.pushTimer)
    for (const write of this.writes.values()) clearTimeout(write.timer)
  }

  edit(id: string, content: string) {
    const existing = this.writes.get(id)
    if (existing) clearTimeout(existing.timer)
    this.writes.set(id, { content, timer: setTimeout(() => { void this.flushEdit(id) }, WRITE_DELAY_MS) })
  }

  isWriting(id: string) { return this.writes.has(id) }
  hasUnwritten() { return this.writes.size > 0 }

  async flush() {
    await Promise.all([...this.writes.keys()].map(id => this.flushEdit(id)))
  }

  syncSoon(delay = PUSH_DELAY_MS) {
    clearTimeout(this.pushTimer)
    this.pushTimer = setTimeout(() => { void this.syncNow() }, delay)
  }

  syncNow(): Promise<void> {
    if (this.stopped) return Promise.resolve()
    if (this.passing) { this.again = true; return this.current }
    this.current = this.pass()
    return this.current
  }

  async useServer(id: string) {
    const { mirror, listener } = this.context
    this.cancelWrite(id)
    await mirror.deletePending(this.slug, id)
    const document = await mirror.getDocument(this.slug, id)
    if (document) listener.onDocument(document, undefined)
    else listener.onRemoved(id)
    await this.refreshPending()
  }

  async keepMine(id: string) {
    const { mirror, listener } = this.context
    await this.flushEdit(id)
    const pending = await mirror.getPending(this.slug, id)
    if (!pending || !pending.conflict || pending.conflict === 'deleted') return
    pending.baseRevision = pending.conflict.document.revision
    pending.conflict = null
    await mirror.putPending(pending)
    const document = await mirror.getDocument(this.slug, id)
    if (document) listener.onDocument(document, pending)
    await this.syncNow()
  }

  async discard(id: string) {
    const { mirror, listener } = this.context
    this.cancelWrite(id)
    await mirror.deletePending(this.slug, id)
    await mirror.deleteDocument(this.slug, id)
    listener.onRemoved(id)
    await this.refreshPending()
  }

  private async pass() {
    this.passing = true
    clearTimeout(this.pushTimer)
    this.setStatus({ syncing: true })
    try {
      await this.flush()
      await this.locked(async () => {
        const replayed = await this.replayer.replay()
        await this.puller.pull()
        const pushed = await this.pusher.push()
        if (replayed || pushed) await publishView(this.context)
      })
      this.backoff = MIN_BACKOFF_MS
      this.setStatus({ online: true, error: '', lastSync: new Date().toISOString() })
      this.channel?.postMessage({ slug: this.slug })
      this.schedule(FULL_SYNC_MS)
    } catch (ex) {
      if (isOffline(ex)) {
        this.setStatus({ online: false })
        this.schedule(this.backoff)
        this.backoff = Math.min(this.backoff * 2, MAX_BACKOFF_MS)
      } else {
        this.setStatus({ error: ex instanceof Error ? ex.message : 'Sync failed.' })
        this.schedule(FULL_SYNC_MS)
      }
    } finally {
      await this.refreshPending({ syncing: false })
      this.passing = false
      if (this.again) { this.again = false; void this.syncNow() }
    }
  }

  private connectEvents() {
    this.events?.close()
    this.events = new EventSource(this.context.api.eventsUrl(this.slug))
    this.events.onopen = () => { void this.syncNow() }
    this.events.addEventListener('workspace', () => { void this.syncNow() })
  }

  private onOnline = () => { this.backoff = MIN_BACKOFF_MS; void this.syncNow() }
  private onVisible = () => {
    if (document.visibilityState === 'hidden') void this.flush()
    else void this.syncNow()
  }

  private schedule(delay: number) {
    clearTimeout(this.timer)
    this.timer = setTimeout(() => { void this.syncNow() }, delay)
  }

  private setStatus(patch: Partial<SyncStatus>) {
    this.status = { ...this.status, ...patch }
    this.context.listener.onStatus(this.status)
  }

  private async refreshPending(patch: Partial<SyncStatus> = {}) {
    this.setStatus({ ...patch, pending: await countPending(this.context) })
  }

  private cancelWrite(id: string) {
    const write = this.writes.get(id)
    if (write) { clearTimeout(write.timer); this.writes.delete(id) }
  }

  private async flushEdit(id: string) {
    const write = this.writes.get(id)
    if (!write) return
    clearTimeout(write.timer)
    this.writes.delete(id)
    const { mirror } = this.context
    const document = await mirror.getDocument(this.slug, id)
    const existing = await mirror.getPending(this.slug, id)
    if (existing?.conflict) {
      // The writer is revising their side of a conflict; keep the conflict, update the draft.
      existing.content = write.content
      existing.updated = new Date().toISOString()
      await mirror.putPending(existing)
      return
    }
    if (document && write.content === document.content) {
      if (existing) await mirror.deletePending(this.slug, id)
    } else if (document || existing) {
      await mirror.putPending({
        slug: this.slug, id, content: write.content, updated: new Date().toISOString(), conflict: null,
        baseRevision: existing?.baseRevision ?? document!.document.revision,
      })
    }
    await this.refreshPending()
    this.syncSoon()
  }

  /** One pass per project at a time across tabs; without Web Locks, passes in this tab are already serialized. */
  private async locked(run: () => Promise<void>): Promise<void> {
    const locks = navigator.locks
    if (locks) await locks.request(`odysseum-sync:${this.slug}`, run)
    else await run()
  }
}
