import { markRaw } from 'vue'
import IconGrid from '~icons/lucide/grid-3x3'
import IconLayoutGrid from '~icons/lucide/layout-grid'
import IconList from '~icons/lucide/list'
import './style.css'
import { useHost } from './host'
import type { OdysseumPluginApi } from './odysseum'
import BoardView from './views/BoardView.vue'
import GridView from './views/GridView.vue'
import OutlineView from './views/OutlineView.vue'

/** The views Odysseum ships with besides the editor. The server half (ViewsPlugin.cs) declares the same names. */
export default function register(odysseum: OdysseumPluginApi) {
  useHost(odysseum)
  odysseum.registerView({ name: 'board', label: 'Corkboard', icon: markRaw(IconLayoutGrid), component: markRaw(BoardView) })
  odysseum.registerView({ name: 'outline', label: 'Outline', icon: markRaw(IconList), component: markRaw(OutlineView) })
  odysseum.registerView({ name: 'grid', label: 'Grid', icon: markRaw(IconGrid), component: markRaw(GridView) })
}
