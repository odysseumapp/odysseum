<script setup lang="ts">
import { computed, ref } from 'vue'
import IconChevronDown from '~icons/lucide/chevron-down'
import IconChevronRight from '~icons/lucide/chevron-right'
import IconPencil from '~icons/lucide/pencil'
import IconPlus from '~icons/lucide/plus'
import { ui } from '../host'
import type { DocumentSummary, Folder, ViewEmits, ViewItem, ViewProps } from '../odysseum'
import { descendantDocuments, documentItem, findFolder, folderDocument, folderItems, folderPath, isInside, linkBetween } from '../structure'
import GridLink from './GridLink.vue'

const props = defineProps<ViewProps>()
const emit = defineEmits<ViewEmits>()
const { Button, Icon, ItemCard, Select, Textarea } = ui()

/** The folder whose documents are the columns: the chosen one (setting `columnFolder`), or Threads, Manuscript or another
 *  top-level folder outside this one. */
const columnFolder = computed(() => {
  const chosen = typeof props.settings.columnFolder === 'string' ? findFolder(props.project, props.settings.columnFolder) : undefined
  if (chosen) return chosen
  const top = props.project.folders.filter(item => item.parentFolderId === props.project.project.rootFolderId)
  const named = (name: string) => top.find(item => item.name.toLocaleLowerCase() === name)
  return [named('threads'), named('manuscript'), ...top].find(item => item && !isInside(props.project, props.folder.id, item.id))
})
const columns = computed(() => columnFolder.value ? descendantDocuments(props.project, columnFolder.value.id) : [])
const folderChoices = computed(() => props.project.folders.filter(item => item.parentFolderId && item.id !== props.folder.id)
  .map(item => ({ label: folderPath(props.project, item.id), value: item.id })))
type Row = { doc: DocumentSummary; depth: number; group?: never; members?: never; own?: never } | { group: Folder; depth: number; members: DocumentSummary[]; own?: DocumentSummary; doc?: never }
const collapsed = ref(new Set<string>())
const rows = computed<Row[]>(() => {
  const walk = (items: ViewItem[], depth: number): Row[] => items.flatMap((item): Row[] => item.document
    ? [{ doc: item.document, depth }]
    : [{ group: item.folder, depth, members: descendantDocuments(props.project, item.folder.id), own: folderDocument(props.project, item.folder) },
        ...(collapsed.value.has(item.folder.id) ? [] : walk(folderItems(props.project, item.folder.id), depth + 1))])
  return walk(props.items, 0)
})
function toggleGroup(id: string) {
  if (collapsed.value.has(id)) collapsed.value.delete(id)
  else collapsed.value.add(id)
}
const linkOf = (doc: DocumentSummary, col: DocumentSummary) => linkBetween(props.project, doc.id, col.id)
function toggle(doc: DocumentSummary, col: DocumentSummary) {
  const link = linkOf(doc, col)
  if (link) emit('unlink', link.id)
  else emit('link', doc.id, col.id)
}
function annotate(doc: DocumentSummary, col: DocumentSummary, note: string) {
  const link = linkOf(doc, col)
  if (link) emit('setLinkNote', link.id, note)
}
const editing = ref('')
const draft = ref('')
function editSynopsis(doc: DocumentSummary) { editing.value = doc.id; draft.value = doc.synopsis }
function finishSynopsis(doc: DocumentSummary, keep: boolean) {
  if (editing.value !== doc.id) return
  editing.value = ''
  if (keep && draft.value.trim() !== doc.synopsis.trim()) emit('updateDetails', doc.id, { synopsis: draft.value.trim() })
}
const choose = (id: unknown) => { if (typeof id === 'string' && id !== columnFolder.value?.id) emit('saveSettings', { ...props.settings, columnFolder: id }) }
</script>

<template>
  <div class="vw:space-y-4 vw:min-w-0">
    <div class="vw:flex vw:flex-wrap vw:items-center vw:gap-2">
      <p class="vw:text-sm vw:text-muted vw:mr-auto">Rows are the documents in {{ folder.parentFolderId ? folder.name : 'the project' }}; columns are the documents in the folder you pick. A mark sits where the two are linked; click it to leave a note there.</p>
      <Select :model-value="columnFolder?.id" :items="folderChoices" aria-label="Columns from folder" placeholder="Pick a folder" class="vw:w-56" @update:model-value="choose" />
      <Button v-if="columnFolder" :icon="IconPlus" variant="soft" @click="emit('createDocument', columnFolder.id)">New in {{ columnFolder.name }}</Button>
    </div>
    <p v-if="!rows.length" class="vw:text-muted vw:py-8">This folder is empty. Add a document or subfolder.</p>
    <p v-else-if="!columns.length" class="vw:text-muted vw:py-8">{{ columnFolder ? `${columnFolder.name} has no documents yet.` : 'Add another folder to compare against.' }}</p>
    <div v-else class="vw:overflow-auto vw:rounded-lg vw:border vw:border-default vw:max-h-[65dvh]">
      <table class="vw:border-collapse vw:text-sm" aria-label="Grid">
        <thead>
          <tr>
            <th class="vw:sticky vw:top-0 vw:left-0 vw:z-20 vw:bg-default vw:border-b vw:border-r vw:border-default vw:min-w-56 vw:p-2"><span class="vw:sr-only">Document</span></th>
            <th v-for="col in columns" :key="col.id" scope="col" class="vw:sticky vw:top-0 vw:z-10 vw:bg-default vw:border-b vw:border-r vw:border-default vw:min-w-40 vw:p-2 vw:align-top vw:text-left vw:font-normal">
              <button type="button" class="vw:flex vw:items-center vw:gap-2 vw:w-full vw:text-left vw:font-medium vw:focus-visible:outline-2 vw:focus-visible:outline-primary" :aria-label="`Open ${col.title}`" @click="emit('open', col.id)">
                <Icon :item="col" class="vw:size-4 vw:shrink-0 vw:text-primary" /><span class="vw:truncate">{{ col.title }}</span>
              </button>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.group ? `group:${row.group.id}` : row.doc.id">
            <template v-if="row.group">
              <th scope="row" class="vw:sticky vw:left-0 vw:z-10 vw:bg-elevated vw:border-b vw:border-r vw:border-default vw:p-2 vw:text-left vw:font-medium" :style="{ paddingLeft: `${8 + row.depth * 16}px` }">
                <div class="vw:flex vw:items-center vw:gap-1">
                  <Button color="neutral" variant="ghost" size="xs" :icon="collapsed.has(row.group.id) ? IconChevronRight : IconChevronDown" :aria-label="`${collapsed.has(row.group.id) ? 'Expand' : 'Collapse'} ${row.group.name}`" :aria-expanded="!collapsed.has(row.group.id)" @click="toggleGroup(row.group.id)" />
                  <Button color="neutral" variant="link" class="vw:min-w-0" :aria-label="`Open folder ${row.group.name}`" @click="emit('open', row.group.id)"><span class="vw:truncate">{{ row.group.name }}</span></Button>
                  <span class="vw:text-xs vw:text-muted vw:ml-auto">{{ row.members.length }}</span>
                </div>
              </th>
              <td v-for="col in columns" :key="col.id" class="vw:relative vw:bg-elevated vw:border-b vw:border-r vw:border-default vw:p-2 vw:align-middle">
                <div class="vw:absolute vw:inset-y-0 vw:left-1/2 vw:w-1 vw:-translate-x-1/2 vw:bg-primary/30" aria-hidden="true" />
                <div class="vw:relative vw:flex vw:flex-wrap vw:items-center vw:justify-center vw:gap-1">
                  <GridLink v-if="row.own && row.own.id !== col.id" :link="linkOf(row.own, col)" :col="col" :name="row.group.name" @toggle="toggle(row.own, col)" @note="note => annotate(row.own!, col, note)" />
                  <template v-if="collapsed.has(row.group.id)">
                    <Button v-for="doc in row.members.filter(member => linkOf(member, col))" :key="doc.id" size="xs" color="primary" variant="soft" class="vw:max-w-full" :aria-label="`Open ${doc.title}`" @click="emit('open', doc.id)"><span class="vw:truncate">{{ doc.title }}</span></Button>
                  </template>
                </div>
              </td>
            </template>
            <template v-else>
              <th scope="row" class="vw:sticky vw:left-0 vw:z-10 vw:bg-default vw:border-b vw:border-r vw:border-default vw:p-2 vw:align-middle vw:text-left vw:font-normal vw:w-72 vw:max-w-72" :style="{ paddingLeft: `${8 + row.depth * 16}px` }">
                <ItemCard :item="documentItem(row.doc)" compact @open="emit('open', row.doc.id)">
                  <Textarea
                    v-if="editing === row.doc.id" v-model="draft" autofocus autoresize :rows="2" :maxrows="8" size="sm" class="vw:w-full vw:mt-2"
                    :aria-label="`Synopsis of ${row.doc.title}`" placeholder="What happens here?"
                    @blur="finishSynopsis(row.doc, true)" @keydown.enter.exact.prevent="finishSynopsis(row.doc, true)" @keydown.esc.prevent.stop="finishSynopsis(row.doc, false)"
                  />
                  <button
                    v-else type="button" class="vw:group vw:flex vw:items-start vw:gap-1 vw:w-full vw:mt-2 vw:text-left vw:text-xs vw:text-muted vw:hover:text-default vw:rounded vw:focus-visible:outline-2 vw:focus-visible:outline-primary"
                    :aria-label="`Edit synopsis of ${row.doc.title}`" title="Edit synopsis" @click="editSynopsis(row.doc)"
                  >
                    <span class="vw:line-clamp-3 vw:whitespace-pre-line vw:break-words" :class="{ 'vw:italic': !row.doc.synopsis }">{{ row.doc.synopsis || 'Add a synopsis' }}</span>
                    <IconPencil class="vw:size-3 vw:shrink-0 vw:mt-0.5 vw:opacity-0 vw:group-hover:opacity-100 vw:group-focus-visible:opacity-100" aria-hidden="true" />
                  </button>
                </ItemCard>
              </th>
              <td v-for="col in columns" :key="col.id" class="vw:relative vw:border-b vw:border-r vw:border-default vw:p-2 vw:align-middle">
                <div class="vw:absolute vw:inset-y-0 vw:left-1/2 vw:w-1 vw:-translate-x-1/2 vw:bg-primary/30" aria-hidden="true" />
                <div v-if="col.id === row.doc.id" class="vw:relative" />
                <GridLink v-else :link="linkOf(row.doc, col)" :col="col" :name="row.doc.title" @toggle="toggle(row.doc, col)" @note="note => annotate(row.doc, col, note)" />
              </td>
            </template>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
