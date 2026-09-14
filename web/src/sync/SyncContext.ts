import type { IOdysseumApi } from '../api/IOdysseumApi'
import type { IMirrorStore } from '../storage'
import type { ISyncListener } from './ISyncEngine'

/** What every sync step works with. `slug` can change once, when the server names a project created offline. */
export interface SyncContext {
  readonly api: IOdysseumApi
  readonly mirror: IMirrorStore
  slug: string
  readonly listener: ISyncListener
}
