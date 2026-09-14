import type { IOdysseumApi } from '../api/IOdysseumApi'
import { isOffline } from '../api/IApiClient'
import type { Snapshot } from '../models'
import type { IMirrorStore } from '../storage'

/** Version history lives on the server; what has been fetched on this device stays readable offline. */
export class HistoryCache {
  constructor(private readonly api: IOdysseumApi, private readonly mirror: IMirrorStore) {}

  /** Returns the list and whether it came from the server (false: cached, possibly stale). */
  async list(slug: string, id: string): Promise<{ snapshots: Snapshot[]; fresh: boolean }> {
    try {
      const snapshots = await this.api.listSnapshots(slug, id)
      const cached = await this.mirror.getSnapshots(slug, id)
      await this.mirror.putSnapshots({ slug, id, list: snapshots, contents: cached?.contents ?? {} })
      return { snapshots, fresh: true }
    } catch (ex) {
      if (!isOffline(ex)) throw ex
      return { snapshots: (await this.mirror.getSnapshots(slug, id))?.list ?? [], fresh: false }
    }
  }

  async read(slug: string, id: string, snapshot: string): Promise<string> {
    const cached = await this.mirror.getSnapshots(slug, id)
    if (cached?.contents[snapshot] !== undefined) return cached.contents[snapshot]
    const content = await this.api.getSnapshot(slug, id, snapshot)
    await this.mirror.putSnapshots({ slug, id, list: cached?.list ?? [], contents: { ...cached?.contents, [snapshot]: content } })
    return content
  }
}
