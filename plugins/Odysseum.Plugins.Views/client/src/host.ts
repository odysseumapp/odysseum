import type { ComponentKit, OdysseumPluginApi } from './odysseum'

let api: OdysseumPluginApi | undefined

export function useHost(value: OdysseumPluginApi) { api = value }

/** The host's shared components. Views mount after `register`, so the API is always there when they ask. */
export function ui(): ComponentKit {
  if (!api) throw new Error('The views plugin is not registered.')
  return api.ui
}
