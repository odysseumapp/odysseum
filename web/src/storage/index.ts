import type { IMirrorStore } from './IMirrorStore'
import { DB_NAME, DB_VERSION, IndexedDbMirrorStore, upgrade } from './IndexedDbMirrorStore'
import { MemoryMirrorStore } from './MemoryMirrorStore'

export type * from './IMirrorStore'

/** Durable storage when the browser allows it, in-memory otherwise; callers check `durable` if it matters. */
export async function openMirrorStore(): Promise<IMirrorStore> {
  try {
    if (!('indexedDB' in globalThis)) return new MemoryMirrorStore()
    const open = indexedDB.open(DB_NAME, DB_VERSION)
    open.onupgradeneeded = () => upgrade(open.result)
    const db = await new Promise<IDBDatabase>((resolve, reject) => {
      open.onsuccess = () => resolve(open.result)
      open.onerror = () => reject(open.error)
    })
    // Ask the browser not to evict the manuscript under storage pressure. Best effort; installed apps get it automatically.
    void navigator.storage?.persist?.().catch(() => undefined)
    return new IndexedDbMirrorStore(db)
  } catch {
    return new MemoryMirrorStore()
  }
}
