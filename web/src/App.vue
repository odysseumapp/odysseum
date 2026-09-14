<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ArrowDown, ArrowDownToLine, ArrowUp, ArrowUpRight, Bold, BookOpen, Check, ChevronDown, ChevronRight, Clock3, Code2, Feather, FileText, Folder, GitBranch, GripVertical, Heading2, Italic, LayoutGrid, List, ListTree, Loader2, LockKeyhole, MapPin, Maximize2, Minimize2, Minus, NotebookPen, PanelLeft, PanelRight, Plus, Quote, Redo2, RefreshCw, Search, Settings2, Undo2, UserRound, Users, X } from '@lucide/vue'
const ManuscriptEditor = defineAsyncComponent(() => import('./components/ManuscriptEditor.vue'))
import AppDialog from './components/AppDialog.vue'
import Logo from './components/Logo.vue'
import DocumentLinkPicker from './components/DocumentLinkPicker.vue'
import ArcsView from './components/ArcsView.vue'
import { useWorkspace } from './composables/useWorkspace'
import { countWords, statusLabel, type DocumentSummary, type Snapshot } from './models'

const workspace = useWorkspace()
const { projects, slug, project, selectedId, active, error, notice, sync, connected, authenticated, passwordRequired, loading } = workspace
const view = ref<'write' | 'board' | 'outline' | 'arcs'>('write')
const section = ref<'manuscript' | 'notes' | 'characters' | 'locations' | 'arcs'>('manuscript')
const inspector = ref(window.innerWidth > 1100)
const binder = ref(window.innerWidth > 760)
const focus = ref(false)
const source = ref(false)
const reading = ref(false)
const fontSize = ref(19)
const editor = ref<{ format: (type: string) => void }>()
const dialog = ref<'' | 'new' | 'search' | 'settings' | 'history' | 'conflict' | 'move' | 'projects'>('')
const busy = ref(false)
const dialogError = ref('')
const collapsed = reactive(new Set<string>())
const newTitle = ref('')
const newProjectTitle = ref('')
const newFolder = ref('Manuscript')
const movedPath = ref('')
const projectTitle = ref('')
const projectGoal = ref(50000)
const projectSceneGoal = ref(1000)
const password = ref('')
const loginError = ref('')
const search = ref('')
const searching = ref(false)
const results = ref<{ document: DocumentSummary; excerpt: string }[]>([])
const snapshots = ref<Snapshot[]>([])
const snapshotText = ref<string | null>(null)
const historyNote = ref('')
const dragId = ref('')
const dropId = ref('')
const metadataFields = ['title', 'synopsis', 'notes', 'status', 'wordGoal', 'characters', 'locations', 'arcPositions'] as const
type Details = Pick<DocumentSummary, typeof metadataFields[number]>
interface DetailsDraft { fields: Details; base: Details; dirty: boolean }
const detailDrafts = reactive(new Map<string, DetailsDraft>())
const details = computed(() => detailDrafts.get(selectedId.value))
const detailSaving = ref(false)
const rendered = ref('')
let renderGeneration = 0
watch([reading, () => active.value?.content], async ([show, content]) => {
  const generation = ++renderGeneration
  if (!show) return
  const { default: MarkdownIt } = await import('markdown-it')
  const markdown = new MarkdownIt({ html: false, linkify: true, typographer: false })
  if (generation === renderGeneration) rendered.value = markdown.render(content ?? '')
})
// Each document kind has its own binder section; a new kind means a new entry here and a folder rule on the server.
const sectionOf = (doc: DocumentSummary): typeof section.value => doc.kind === 'arc' ? 'arcs' : doc.kind === 'location' ? 'locations' : doc.kind === 'character' ? 'characters' : doc.kind === 'note' ? 'notes' : 'manuscript'
const sectionNoun = computed(() => section.value === 'arcs' ? 'arc' : section.value === 'locations' ? 'location' : section.value === 'characters' ? 'character' : section.value === 'notes' ? 'note' : 'scene')
const documents = computed(() => project.value?.documents.filter(doc => sectionOf(doc) === section.value) ?? [])
const characters = computed(() => project.value?.documents.filter(doc => doc.kind === 'character') ?? [])
const locations = computed(() => project.value?.documents.filter(doc => doc.kind === 'location') ?? [])
const arcs = computed(() => project.value?.documents.filter(doc => doc.kind === 'arc') ?? [])
const arcDocuments = computed(() => project.value?.documents.filter(doc => doc.kind !== 'arc') ?? [])
const arcSaving = ref(false)
const characterName = (id: string) => characters.value.find(doc => doc.id === id)?.title
const castOf = (doc: DocumentSummary) => doc.characters.map(characterName).filter(Boolean).join(', ')
const appearances = computed(() => project.value?.documents.filter(doc => doc.kind === 'scene' && (active.value?.document.kind === 'location' ? doc.locations ?? [] : doc.characters).includes(selectedId.value)) ?? [])
const groups = computed(() => {
  const map = new Map<string, DocumentSummary[]>()
  for (const doc of documents.value) {
    const group = doc.folder || 'Unfiled'
    if (!map.has(group)) map.set(group, [])
    map.get(group)!.push(doc)
  }
  return [...map].map(([path, documents]) => ({ path, title: path.split('/').at(-1)!, documents }))
})
const folders = computed(() => [...new Set(project.value?.documents.map(doc => doc.folder) ?? [])])
const words = computed(() => countWords(active.value?.content ?? ''))
const totalWords = computed(() => (project.value?.documents ?? []).filter(doc => doc.kind === 'scene').reduce((total, doc) => total + (doc.id === selectedId.value && active.value ? words.value : doc.wordCount), 0))
const progress = computed(() => Math.min(100, totalWords.value / (project.value?.settings.wordGoal || 1) * 100))
const savedState = computed(() => {
  if (!active.value) return 'Ready to write'
  if (active.value.conflict) return 'Review changes'
  if (active.value.error || sync.value.error) return 'Save interrupted'
  if (!workspace.dirty(active.value)) return 'All changes saved'
  if (!sync.value.online) return 'Saved on this device'
  return 'Saving…'
})
const formatActions = [
  { type: 'undo', title: 'Undo', icon: Undo2 }, { type: 'redo', title: 'Redo', icon: Redo2 },
  { type: 'bold', title: 'Bold (Ctrl/⌘ B)', icon: Bold }, { type: 'italic', title: 'Italic (Ctrl/⌘ I)', icon: Italic },
  { type: 'heading', title: 'Heading', icon: Heading2 }, { type: 'quote', title: 'Blockquote', icon: Quote },
  { type: 'list', title: 'Bulleted list', icon: List }, { type: 'break', title: 'Scene break', icon: Minus },
]
const number = (value: number) => value.toLocaleString()
const date = (value: string) => new Date(value).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const fields = (document: DocumentSummary): Details => ({ title: document.title, synopsis: document.synopsis, notes: document.notes, status: document.status, wordGoal: document.wordGoal, characters: [...(document.characters ?? [])], locations: [...(document.locations ?? [])], arcPositions: { ...(document.arcPositions ?? {}) } })

watch(() => active.value?.document, document => {
  if (!document) return
  let draft = detailDrafts.get(document.id)
  if (!draft) {
    try {
      const stored = localStorage.getItem(`odysseum:${project.value?.id}:details:${document.id}`)
      if (stored) {
        draft = JSON.parse(stored) as DetailsDraft
        draft.fields.locations ??= []; draft.base.locations ??= []
        draft.fields.arcPositions ??= {}; draft.base.arcPositions ??= {}
      }
    } catch { /* Keep the disk metadata when browser storage is unavailable. */ }
  }
  if (!draft?.dirty) draft = { fields: fields(document), base: fields(document), dirty: false }
  detailDrafts.set(document.id, draft)
}, { immediate: true })
watch(dialog, value => { dialogError.value = ''; if (value === 'projects') workspace.loadProjects().catch(workspace.showError) })
watch(section, value => { if (value !== 'arcs' && view.value === 'arcs') view.value = 'write' })
watch(slug, () => { section.value = 'manuscript'; view.value = 'write'; reading.value = false; collapsed.clear() })
watch(() => project.value?.settings.title, title => { document.title = title ? `${title} — Odysseum` : 'Odysseum — A place for your words' })
let searchTimer: ReturnType<typeof setTimeout>
watch(search, query => {
  clearTimeout(searchTimer)
  if (!query.trim()) { results.value = []; searching.value = false; return }
  searching.value = true
  // Search runs over the local copy, so it works the same with or without the server.
  searchTimer = setTimeout(() => { results.value = workspace.search(query); searching.value = false }, 150)
})
// Details the server refused come back as an unsaved draft so nothing typed is lost.
watch(workspace.rejectedDetails, rejected => {
  for (const [id, rejectedFields] of rejected) {
    const current = project.value?.documents.find(doc => doc.id === id)
    const draft = { fields: { ...rejectedFields }, base: current ? fields(current) : { ...rejectedFields }, dirty: true }
    detailDrafts.set(id, draft)
    try { localStorage.setItem(`odysseum:${project.value?.id}:details:${id}`, JSON.stringify(draft)) } catch { /* Kept in memory. */ }
    rejected.delete(id)
  }
})

function editDetails() {
  if (!details.value) return
  details.value.dirty = JSON.stringify(details.value.fields) !== JSON.stringify(details.value.base)
  try { localStorage.setItem(`odysseum:${project.value?.id}:details:${selectedId.value}`, JSON.stringify(details.value)) }
  catch { error.value = 'Browser storage is unavailable. Save your document details before closing this tab.' }
}
function resetDetails() {
  if (!active.value || !details.value) return
  details.value.fields = fields(active.value.document)
  details.value.base = fields(active.value.document)
  editDetails()
}
async function saveDetails() {
  const draft = details.value
  const id = selectedId.value
  if (!draft || !project.value || detailSaving.value) return
  detailSaving.value = true
  const captured = { ...draft.fields }
  try {
    await workspace.saveDetails(id, captured, { ...draft.base })
    draft.base = captured
    draft.dirty = JSON.stringify(draft.fields) !== JSON.stringify(captured)
    localStorage.setItem(`odysseum:${project.value.id}:details:${id}`, JSON.stringify(draft))
  } catch (ex) { workspace.showError(ex) }
  finally { detailSaving.value = false }
}

function setDetailArcs(ids: string[]) {
  if (!details.value) return
  const previous = details.value.fields.arcPositions
  details.value.fields.arcPositions = Object.fromEntries(ids.map(id => [id, previous[id] ?? Math.min(10000,
    Math.floor(Math.max(-1, ...arcDocuments.value.map(doc => doc.arcPositions?.[id] ?? -1))) + 1)]))
  editDetails()
}
async function placeOnArc(document: DocumentSummary, arc: string, position: number | null) {
  if (arcSaving.value) return
  const current = project.value?.documents.find(doc => doc.id === document.id)
  if (!current) return
  const patch = (positions: Record<string, number>) => {
    const next = { ...positions }
    if (position === null) delete next[arc]
    else next[arc] = position
    return next
  }
  arcSaving.value = true
  try {
    const base = fields(current)
    await workspace.saveDetails(current.id, { ...base, arcPositions: patch(base.arcPositions) }, base)
    const draft = detailDrafts.get(current.id)
    if (draft) {
      draft.fields.arcPositions = patch(draft.fields.arcPositions)
      draft.base.arcPositions = patch(draft.base.arcPositions)
      draft.dirty = JSON.stringify(draft.fields) !== JSON.stringify(draft.base)
      localStorage.setItem(`odysseum:${project.value?.id}:details:${current.id}`, JSON.stringify(draft))
    }
  } catch (ex) { workspace.showError(ex) }
  finally { arcSaving.value = false }
}

async function selectDocument(doc: DocumentSummary) {
  section.value = sectionOf(doc)
  view.value = 'write'
  reading.value = false
  await workspace.open(doc.id)
  if (window.innerWidth < 760) binder.value = false
}
function openNew() {
  newTitle.value = ''
  newFolder.value = section.value === 'arcs' ? 'Arcs' : section.value === 'locations' ? 'Locations' : section.value === 'characters' ? 'Characters' : section.value === 'notes' ? 'Notes' : active.value?.document.kind === 'scene' ? active.value.document.folder : 'Manuscript'
  dialog.value = 'new'
}
async function createScene() {
  await action(async () => {
    const doc = await workspace.create(newTitle.value, newFolder.value)
    section.value = sectionOf(doc)
    view.value = doc.kind === 'arc' ? 'arcs' : 'write'
    dialog.value = ''
  })
}
async function createNewProject() {
  await action(async () => {
    await workspace.createProject(newProjectTitle.value)
    newProjectTitle.value = ''
    dialog.value = ''
  })
}
async function switchProject(target: string) {
  dialog.value = ''
  if (target !== slug.value) await workspace.openProject(target)
}
function settings() {
  projectTitle.value = project.value?.settings.title ?? ''
  projectGoal.value = project.value?.settings.wordGoal ?? 50000
  projectSceneGoal.value = project.value?.settings.defaultSceneWordGoal ?? 1000
  dialog.value = 'settings'
}
async function saveSettings() {
  await action(async () => {
    await workspace.updateSettings({ title: projectTitle.value, wordGoal: projectGoal.value, defaultSceneWordGoal: projectSceneGoal.value })
    dialog.value = ''
  })
}
async function action(fn: () => Promise<void>) {
  busy.value = true
  dialogError.value = ''
  try { await fn() }
  catch (ex) { dialogError.value = (ex as Error).message }
  finally { busy.value = false }
}
async function history() {
  if (!active.value) return
  snapshotText.value = null
  snapshots.value = []
  historyNote.value = ''
  dialog.value = 'history'
  await action(async () => {
    const result = await workspace.snapshots(selectedId.value)
    snapshots.value = result.snapshots
    if (!result.fresh) historyNote.value = 'Offline: showing the versions already fetched on this device.'
  })
}
async function previewSnapshot(snapshot: Snapshot) {
  await action(async () => {
    snapshotText.value = await workspace.snapshot(selectedId.value, snapshot.id)
  })
}
function restoreSnapshot() {
  if (snapshotText.value === null) return
  workspace.edit(snapshotText.value)
  dialog.value = ''
  reading.value = false
}
async function moveFile() {
  await action(async () => {
    await workspace.move(movedPath.value)
    dialog.value = ''
  })
}
async function reorder(from: string, to: string) {
  if (!project.value || !from || from === to) return
  const ids = project.value.documents.map(doc => doc.id)
  const sourceIndex = ids.indexOf(from)
  const targetIndex = ids.indexOf(to)
  if (sourceIndex < 0 || targetIndex < 0) return
  ids.splice(sourceIndex, 1)
  ids.splice(targetIndex, 0, from)
  try { await workspace.reorder(ids) }
  catch (ex) { workspace.showError(ex) }
}
function drop(target: string) {
  void reorder(dragId.value, target)
  dragId.value = ''; dropId.value = ''
}
function moveOrder(doc: DocumentSummary, direction: number) {
  const index = documents.value.findIndex(item => item.id === doc.id)
  const neighbor = documents.value[index + direction]
  if (neighbor) void reorder(doc.id, neighbor.id)
}
async function login() {
  busy.value = true
  try { await workspace.login(password.value); password.value = ''; loginError.value = ''; await workspace.start() }
  catch (ex) { loginError.value = (ex as Error).message }
  finally { busy.value = false }
}
async function logout() { await workspace.logout() }
function downloadDraft() {
  if (!active.value) return
  const url = URL.createObjectURL(new Blob([active.value.content], { type: 'text/markdown;charset=utf-8' }))
  const anchor = document.createElement('a')
  anchor.href = url; anchor.download = `${active.value.document.title}-draft.md`; anchor.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
function shortcut(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') { event.preventDefault(); dialog.value = 'search' }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); void workspace.save() }
  if (event.key === 'Escape' && !dialog.value) focus.value = false
}
function beforeUnload(event: BeforeUnloadEvent) {
  workspace.beforeUnload(event)
  if ([...detailDrafts.values()].some(draft => draft.dirty)) event.preventDefault()
}
onMounted(() => { void workspace.start(); window.addEventListener('keydown', shortcut); window.addEventListener('beforeunload', beforeUnload) })
onBeforeUnmount(() => { workspace.stop(); clearTimeout(searchTimer); window.removeEventListener('keydown', shortcut); window.removeEventListener('beforeunload', beforeUnload) })
</script>

<template>
  <div v-if="loading" class="opening"><Feather :size="36" /><span>Opening your workspace…</span><Loader2 class="spin" :size="18" /></div>
  <main v-else-if="!authenticated && passwordRequired" class="lock-screen">
    <form class="lock-card" @submit.prevent="login">
      <div class="brand-mark"><Logo :size="34" /></div><span class="eyebrow">WELCOME BACK</span>
      <h1>Your next chapter awaits.</h1><p>Enter your workspace password to settle in.</p>
      <label for="password">Workspace password</label><input id="password" v-model="password" type="password" autocomplete="current-password" autofocus required />
      <p v-if="loginError" class="error-text" role="alert">{{ loginError }}</p>
      <button class="primary-button" :disabled="busy"><LockKeyhole :size="16" /> {{ busy ? 'Unlocking…' : 'Open workspace' }}</button>
    </form>
  </main>
  <main v-else-if="!project" class="library">
    <div class="library-card">
      <div class="brand"><div class="brand-mark"><Logo :size="26" /></div><span class="wordmark">Odysseum</span></div>
      <span class="eyebrow">YOUR WORKSPACE</span>
      <h1>{{ projects.length ? 'Which story today?' : 'Begin your first project.' }}</h1>
      <p>{{ projects.length ? 'Every project is a folder of Markdown files in your workspace.' : 'A project is a folder in your workspace. Give it a name and start writing.' }}</p>
      <p v-if="error" class="error-text" role="alert">{{ error }}</p>
      <div v-if="projects.length" class="project-list" aria-label="Projects">
        <button v-for="item in projects" :key="item.slug" @click="switchProject(item.slug)"><span class="book-cover"><BookOpen :size="18" /></span><span><strong>{{ item.title }}</strong><small>{{ item.slug }}/</small></span><ArrowUpRight :size="15" /></button>
      </div>
      <form class="new-project" @submit.prevent="createNewProject"><label class="field-label" for="library-title">New project</label><div><input id="library-title" v-model="newProjectTitle" placeholder="An untitled novel" required maxlength="200" /><button class="primary-button" :disabled="busy"><Plus :size="16" />{{ busy ? 'Creating…' : 'Create project' }}</button></div><p v-if="dialogError" class="error-text" role="alert">{{ dialogError }}</p></form>
      <div class="library-actions"><button class="text-button" @click="workspace.start"><RefreshCw :size="13" />Refresh</button><button v-if="passwordRequired" class="text-button" @click="logout"><LockKeyhole :size="13" />Lock workspace</button></div>
    </div>
  </main>
  <div v-else class="app-shell" :class="{ focused: focus, 'hide-binder': !binder, 'hide-inspector': !inspector || view !== 'write' || !active }">
    <aside class="binder" aria-label="Project binder">
      <div class="brand"><div class="brand-mark"><Logo :size="26" /></div><span class="wordmark">Odysseum</span><button class="icon-button binder-close" title="Hide binder" aria-label="Hide binder" @click="binder = false"><PanelLeft :size="17" /></button></div>
      <button class="project-switcher" aria-label="Switch project" @click="dialog = 'projects'"><span class="book-cover"><BookOpen :size="22" /></span><span><small>YOUR PROJECT</small><strong>{{ project.settings.title }}</strong></span><ChevronDown :size="15" /></button>
      <button class="search-trigger" @click="dialog = 'search'"><Search :size="16" /><span>Find anything</span><kbd>⌘ K</kbd></button>
      <nav class="section-nav" aria-label="Project sections">
        <button :class="{ active: section === 'manuscript' }" @click="section = 'manuscript'"><BookOpen :size="17" />Manuscript<span>{{ project.documents.filter(doc => doc.kind === 'scene').length }}</span></button>
        <button :class="{ active: section === 'notes' }" @click="section = 'notes'"><NotebookPen :size="17" />Story notes<span>{{ project.documents.filter(doc => doc.kind === 'note').length }}</span></button>
        <button :class="{ active: section === 'characters' }" @click="section = 'characters'"><Users :size="17" />Characters<span>{{ characters.length }}</span></button>
        <button :class="{ active: section === 'locations' }" @click="section = 'locations'"><MapPin :size="17" />Locations<span>{{ locations.length }}</span></button>
        <button :class="{ active: section === 'arcs' }" @click="section = 'arcs'; view = 'arcs'"><GitBranch :size="17" />Arcs<span>{{ arcs.length }}</span></button>
      </nav>
      <div class="binder-heading"><span>{{ section === 'arcs' ? 'STORY THREADS' : section === 'locations' ? 'WHERE IT HAPPENS' : section === 'characters' ? 'WHO IS IN IT' : section === 'notes' ? 'YOUR STORY WORLD' : 'CHAPTERS & SCENES' }}</span><button class="icon-button" :aria-label="`Add ${sectionNoun}`" :title="`Add ${sectionNoun}`" @click="openNew"><Plus :size="17" /></button></div>
      <div class="binder-tree">
        <div v-for="group in groups" :key="group.path" class="tree-group">
          <button class="folder-row" @click="collapsed.has(group.path) ? collapsed.delete(group.path) : collapsed.add(group.path)" :aria-expanded="!collapsed.has(group.path)"><ChevronRight v-if="collapsed.has(group.path)" :size="13" /><ChevronDown v-else :size="13" /><Folder :size="15" /><span>{{ group.title }}</span><small>{{ group.documents.length }}</small></button>
          <div v-if="!collapsed.has(group.path)" class="tree-children">
            <button v-for="doc in group.documents" :key="doc.id" class="document-row" :class="{ selected: doc.id === selectedId, 'drop-target': dropId === doc.id }" :aria-current="doc.id === selectedId ? 'page' : undefined" draggable="true" @dragstart="dragId = doc.id" @dragover.prevent="dropId = doc.id" @dragleave="dropId = ''" @drop.prevent="drop(doc.id)" @dragend="dragId = ''; dropId = ''" @click="selectDocument(doc)"><FileText :size="15" /><span>{{ doc.title }}</span><i class="status-dot" :class="doc.status" :title="statusLabel(doc.status)" aria-hidden="true" /></button>
          </div>
        </div>
        <div v-if="!documents.length" class="binder-empty">Every story starts somewhere.<button @click="openNew">Add your first {{ sectionNoun }} <Plus :size="13" /></button></div>
        <button class="add-scene" @click="openNew"><Plus :size="15" />New {{ section === 'notes' ? 'story note' : sectionNoun }}</button>
      </div>
      <div class="project-progress"><div><span>Manuscript goal</span><button @click="settings" aria-label="Edit manuscript goal"><Settings2 :size="14" /></button></div><strong>{{ number(totalWords) }}<span> / {{ number(project.settings.wordGoal) }} words</span></strong><div class="progress-track"><span :style="{ width: `${progress}%` }" /></div><p>A little further, one word at a time.</p></div>
      <div class="binder-footer"><span class="connection-dot" :class="{ offline: !connected }" /><span>{{ connected ? 'Connected to your files' : sync.pending ? `Offline · ${sync.pending} change${sync.pending === 1 ? '' : 's'} waiting` : 'Offline · working from this device' }}</span><button class="icon-button" title="Refresh workspace" aria-label="Refresh workspace" @click="workspace.refresh"><RefreshCw :size="13" /></button></div>
    </aside>

    <section class="workspace">
      <header class="workspace-header">
        <div class="breadcrumbs"><button v-if="!binder || focus" class="icon-button" aria-label="Show binder" @click="binder = true; focus = false"><PanelLeft :size="18" /></button><BookOpen :size="16" /><span>{{ section === 'arcs' ? 'Arcs' : section === 'locations' ? 'Locations' : section === 'characters' ? 'Characters' : section === 'notes' ? 'Story notes' : 'Manuscript' }}</span><ChevronRight :size="13" /><strong>{{ view === 'write' ? active?.document.folder.split('/').at(-1) || 'Writing desk' : view === 'arcs' ? 'Story arcs' : view === 'board' ? 'Corkboard' : 'Outline' }}</strong></div>
        <div class="header-actions"><button class="text-button export-button" title="Download the manuscript in scene order, including edits not yet synced" @click="workspace.exportManuscript()"><ArrowDownToLine :size="15" /><span>Export</span></button><span class="header-divider" /><button class="icon-button" :class="{ on: focus }" :title="focus ? 'Leave focus mode' : 'Focus mode'" :aria-label="focus ? 'Leave focus mode' : 'Focus mode'" @click="focus = !focus"><Minimize2 v-if="focus" :size="17" /><Maximize2 v-else :size="17" /></button><button class="icon-button" :class="{ on: inspector }" title="Toggle inspector" aria-label="Toggle inspector" @click="inspector = !inspector"><PanelRight :size="17" /></button></div>
      </header>
      <div class="view-bar"><div class="view-tabs" role="tablist" aria-label="Workspace view"><button :class="{ active: view === 'write' }" role="tab" :aria-selected="view === 'write'" @click="view = 'write'"><Feather :size="15" />Write</button><button :class="{ active: view === 'board' }" role="tab" :aria-selected="view === 'board'" @click="view = 'board'"><LayoutGrid :size="15" />Corkboard</button><button :class="{ active: view === 'outline' }" role="tab" :aria-selected="view === 'outline'" @click="view = 'outline'"><ListTree :size="16" />Outline</button><button :class="{ active: view === 'arcs' }" role="tab" :aria-selected="view === 'arcs'" @click="section = 'arcs'; view = 'arcs'"><GitBranch :size="16" />Arcs</button></div><span class="view-caption">{{ documents.length }} {{ sectionNoun }}{{ documents.length === 1 ? '' : 's' }}<span>·</span>{{ number(documents.reduce((n, doc) => n + doc.wordCount, 0)) }} words</span></div>
      <div v-if="error || project.warning" class="notice-bar warning" role="alert"><span>{{ error || project.warning }}</span><button class="icon-button" aria-label="Retry workspace refresh" @click="workspace.refresh"><RefreshCw :size="15" /></button></div>
      <template v-if="view === 'write' && active">
        <div class="formatting-toolbar" aria-label="Formatting"><div class="format-buttons"><button v-for="(item, index) in formatActions" :key="item.type" class="icon-button" :class="{ 'format-gap': index === 2 }" :title="item.title" :aria-label="item.title" :disabled="reading" @mousedown.prevent @click="editor?.format(item.type)"><component :is="item.icon" :size="16" /></button></div><div class="editor-modes"><select v-model.number="fontSize" aria-label="Writing text size"><option :value="17">Aa · 17</option><option :value="19">Aa · 19</option><option :value="22">Aa · 22</option><option :value="25">Aa · 25</option></select><button class="icon-button" :class="{ on: source }" aria-label="Toggle Markdown source" title="Markdown source" @click="source = !source; reading = false"><Code2 :size="17" /></button><button class="text-button" :class="{ on: reading }" @click="reading = !reading">{{ reading ? 'Edit' : 'Read' }}</button></div></div>
        <div v-if="active.conflict" class="notice-bar warning"><span>{{ active.conflict === 'deleted' ? 'This file was removed. Your text is still here.' : 'This file changed elsewhere. Both versions are safe to review.' }}</span><button class="small-button" @click="dialog = 'conflict'">Review changes</button></div>
        <div v-if="active.error || sync.error" class="notice-bar warning"><span>{{ active.error || sync.error }}</span><button class="small-button" @click="workspace.save()">Retry save</button></div>
        <div class="writing-scroll">
          <article class="writing-paper" :style="{ '--writing-size': `${fontSize}px` }">
            <div class="scene-kicker"><span class="scene-number">{{ section === 'arcs' ? 'ARC' : section === 'locations' ? 'LOCATION' : section === 'characters' ? 'CHARACTER' : section === 'notes' ? 'STORY NOTE' : `SCENE ${String(Math.max(1, documents.findIndex(doc => doc.id === selectedId) + 1)).padStart(2, '0')}` }}</span><span class="scene-rule" /><span class="scene-status"><i class="status-dot" :class="active.document.status" />{{ statusLabel(active.document.status) }}</span></div>
            <h1 class="scene-title">{{ active.document.title }}</h1>
            <div v-if="reading" class="rendered-markdown" v-html="rendered" />
            <ManuscriptEditor v-else ref="editor" :document-id="selectedId" :model-value="active.content" :source="source" :font-size="fontSize" @update:model-value="workspace.edit" @save="workspace.save()" />
            <div class="end-mark" aria-hidden="true"><span /><Feather :size="15" /><span /></div>
          </article>
        </div>
        <footer class="writing-footer"><button class="save-indicator" @click="active.conflict ? dialog = 'conflict' : workspace.save()"><Loader2 v-if="active.saving" :size="13" class="spin" /><span v-else-if="active.conflict || active.error || workspace.dirty(active)" class="unsaved-dot" /><Check v-else :size="14" />{{ savedState }}</button><span v-if="notice" class="external-notice">{{ notice }}</span><div class="document-statistics"><strong>{{ number(words) }} <span>words</span></strong><span class="stat-divider">·</span><span>{{ Math.max(1, Math.ceil(words / 225)) }} min read</span></div></footer>
      </template>
      <div v-else-if="view === 'write'" class="empty-desk"><div class="empty-illustration"><Feather :size="40" /></div><span class="eyebrow">MAKE ROOM FOR YOUR STORY</span><h1>A blank page. Endless possibility.</h1><p>Add a scene to begin, or put Markdown files in your workspace.<br />They’ll find their way here automatically.</p><button class="primary-button" @click="openNew"><Plus :size="17" />Write your first scene</button></div>
      <ArcsView v-else-if="view === 'arcs'" :arcs="arcs" :documents="arcDocuments" :busy="arcSaving" @create="section = 'arcs'; openNew()" @open="selectDocument" @place="placeOnArc" />
      <div v-else class="overview-scroll">
        <header class="overview-heading"><div><span class="eyebrow">THE BIG PICTURE</span><h1>{{ section === 'arcs' ? 'Threads through the story' : section === 'locations' ? 'The places in it' : section === 'characters' ? 'The people in it' : section === 'notes' ? 'Your story world' : 'A story taking shape' }}</h1><p>{{ view === 'board' ? 'Give every scene a purpose. Make room for what comes next.' : 'See your manuscript at a glance, one scene at a time.' }}</p></div><button class="primary-button" @click="openNew"><Plus :size="16" />New {{ sectionNoun }}</button></header>
        <div v-if="view === 'board'" class="corkboard">
          <article v-for="(doc, index) in documents" :key="doc.id" class="scene-card" :class="{ 'drop-target': dropId === doc.id }" draggable="true" @dragstart="dragId = doc.id" @dragover.prevent="dropId = doc.id" @dragleave="dropId = ''" @drop.prevent="drop(doc.id)" @dragend="dragId = ''; dropId = ''">
            <div class="card-top"><span>{{ String(index + 1).padStart(2, '0') }}</span><span>{{ doc.folder.split('/').at(-1) || 'Manuscript' }}</span><GripVertical :size="16" /></div>
            <button class="card-open" @click="selectDocument(doc)"><h2>{{ doc.title }}</h2><p :class="{ placeholder: !doc.synopsis }">{{ doc.synopsis || 'A little space to capture what this scene is really about.' }}</p></button>
            <footer><span class="status-pill" :class="doc.status"><i class="status-dot" :class="doc.status" />{{ statusLabel(doc.status) }}</span><span v-if="doc.characters.length" class="card-cast" :title="castOf(doc)"><UserRound :size="11" />{{ castOf(doc) }}</span><span>{{ number(doc.wordCount) }} words</span></footer>
            <div class="card-order"><button class="icon-button" :disabled="index === 0" aria-label="Move scene earlier" @click="moveOrder(doc, -1)"><ArrowUp :size="13" /></button><button class="icon-button" :disabled="index === documents.length - 1" aria-label="Move scene later" @click="moveOrder(doc, 1)"><ArrowDown :size="13" /></button><button class="text-button" @click="selectDocument(doc)">Open scene<ArrowUpRight :size="12" /></button></div>
          </article>
          <button class="new-card" @click="openNew"><Plus :size="25" /><span>The next scene</span><small>Every chapter starts with an idea.</small></button>
        </div>
        <div v-else class="outline-table"><table><thead><tr><th>SCENE</th><th>STATUS</th><th>WORDS</th><th>ORDER</th></tr></thead><tbody><tr v-for="(doc, index) in documents" :key="doc.id"><td><button @click="selectDocument(doc)"><FileText :size="17" /><span><strong>{{ doc.title }}</strong><small>{{ doc.folder }}</small></span></button></td><td><span class="status-pill" :class="doc.status"><i class="status-dot" :class="doc.status" />{{ statusLabel(doc.status) }}</span></td><td>{{ number(doc.wordCount) }}</td><td><button class="icon-button" :disabled="index === 0" aria-label="Move scene earlier" @click="moveOrder(doc, -1)"><ArrowUp :size="14" /></button><button class="icon-button" :disabled="index === documents.length - 1" aria-label="Move scene later" @click="moveOrder(doc, 1)"><ArrowDown :size="14" /></button></td></tr></tbody></table></div>
        <p class="overview-footnote">{{ view === 'board' ? 'Drag cards to change manuscript order. Your file names stay the same.' : 'Use the arrows to arrange your manuscript for export.' }}</p>
      </div>
    </section>

    <aside v-if="active && details" class="inspector" aria-label="Document inspector"><header><span>{{ active.document.kind === 'arc' ? 'ARC' : active.document.kind === 'location' ? 'LOCATION' : active.document.kind === 'character' ? 'CHARACTER' : active.document.kind === 'note' ? 'NOTE' : 'SCENE' }} DETAILS</span><button class="icon-button" aria-label="Close inspector" @click="inspector = false"><PanelRight :size="16" /></button></header><div class="inspector-scroll"><div class="inspector-title"><FileText :size="19" /><span>A little context<br /><strong>for the words ahead.</strong></span></div>
      <label class="field-label" for="scene-title">Title</label><input id="scene-title" v-model="details.fields.title" @input="editDetails" />
      <label class="field-label" for="scene-status">Draft status</label><select id="scene-status" v-model="details.fields.status" @change="editDetails"><option value="draft">First draft</option><option value="revised">In revision</option><option value="done">Finished</option></select>
      <label class="field-label" for="synopsis">Synopsis <span>The scene in a sentence or two</span></label><textarea id="synopsis" v-model="details.fields.synopsis" rows="5" placeholder="What happens here? What changes?" @input="editDetails" />
      <label class="field-label" for="scene-notes">Notes to yourself</label><textarea id="scene-notes" v-model="details.fields.notes" rows="5" placeholder="A detail to remember. A thread to pick up." @input="editDetails" />
      <template v-if="active.document.kind === 'scene'"><label class="field-label">Characters in this scene <span>Tap to attach, then save details</span></label><DocumentLinkPicker kind="character" :model-value="details.fields.characters" :documents="characters" @update:model-value="value => { details!.fields.characters = value; editDetails() }" @create="section = 'characters'; openNew()" />
        <label class="field-label">Locations in this scene <span>Tap to attach, then save details</span></label><DocumentLinkPicker kind="location" :model-value="details.fields.locations" :documents="locations" @update:model-value="value => { details!.fields.locations = value; editDetails() }" @create="section = 'locations'; openNew()" /></template>
      <div v-else-if="active.document.kind === 'character' || active.document.kind === 'location'" class="inspector-section"><span class="section-label">APPEARS IN</span><div class="appearances"><button v-for="scene in appearances" :key="scene.id" class="text-button" @click="selectDocument(scene)">{{ scene.title }}<ArrowUpRight :size="12" /></button><p v-if="!appearances.length" class="field-help">Not in any scene yet. Open a scene and attach this {{ active.document.kind }} under its details.</p></div></div>
      <template v-if="active.document.kind !== 'arc'"><label class="field-label">Story arcs <span>Arrange positions in the Arcs view</span></label><DocumentLinkPicker kind="arc" :model-value="Object.keys(details.fields.arcPositions)" :documents="arcs" @update:model-value="setDetailArcs" @create="section = 'arcs'; openNew()" /></template>
      <button v-else class="text-button" @click="view = 'arcs'; section = 'arcs'"><GitBranch :size="14" />Show arc timelines</button>
      <div v-if="details.dirty" class="details-actions"><button class="small-button" @click="resetDetails">Reset</button><button class="primary-button" :disabled="detailSaving" @click="saveDetails">{{ detailSaving ? 'Saving…' : 'Save details' }}</button></div>
      <div v-if="active.document.kind === 'scene'" class="inspector-section scene-goal"><div class="section-label"><span>SCENE GOAL</span><span>{{ Math.round(Math.min(100, words / (details.fields.wordGoal || 1) * 100)) }}%</span></div><div class="goal-numbers"><strong>{{ number(words) }}</strong><span> / </span><input v-model.number="details.fields.wordGoal" type="number" min="0" max="10000000" aria-label="Scene word goal" @input="editDetails" /><span> words</span></div><div class="progress-track"><span :style="{ width: `${Math.min(100, words / (details.fields.wordGoal || 1) * 100)}%` }" /></div></div>
      <div class="inspector-section"><button class="history-button" @click="history"><span class="history-icon"><Clock3 :size="18" /></span><span><strong>Version history</strong><small>Find an earlier turn of phrase</small></span><ChevronRight :size="15" /></button></div>
      <div class="file-location"><span class="section-label">ON YOUR SHELF</span><code>{{ active.document.path }}</code><button @click="movedPath = active!.document.path; dialog = 'move'">Move or rename file<ArrowUpRight :size="12" /></button><p>Last changed {{ date(active.document.lastModified) }}</p></div>
    </div><footer class="inspector-footer"><Feather :size="14" /><span>Your words. Your files.</span></footer></aside>
  </div>

  <AppDialog v-if="dialog === 'new'" :title="section === 'arcs' ? 'A new arc' : section === 'locations' ? 'A new location' : section === 'characters' ? 'A new character' : section === 'notes' ? 'A new story note' : 'A new scene'" @close="dialog = ''"><form @submit.prevent="createScene"><p class="dialog-description">Give this part of your story a place to begin.</p><label class="field-label" for="new-title">Title</label><input id="new-title" v-model="newTitle" placeholder="An unexpected arrival" autofocus required maxlength="200" /><label class="field-label" for="new-folder">Folder</label><input id="new-folder" v-model="newFolder" list="folder-options" placeholder="Manuscript/Chapter 01" /><datalist id="folder-options"><option v-for="folder in folders" :key="folder" :value="folder" /></datalist><p class="field-help">Use / to create a chapter folder. Leave empty for the project root.</p><p v-if="dialogError" class="error-text" role="alert">{{ dialogError }}</p><div class="dialog-actions"><button type="button" class="small-button" @click="dialog = ''">Cancel</button><button class="primary-button" :disabled="busy"><Plus :size="16" />{{ busy ? 'Creating…' : `Create ${sectionNoun}` }}</button></div></form></AppDialog>
  <AppDialog v-if="dialog === 'projects'" title="Your projects" @close="dialog = ''">
    <p class="dialog-description">Every project is a folder in your workspace with its own manuscript, notes, and history.</p>
    <div class="project-list" aria-label="Projects">
      <button v-for="item in projects" :key="item.slug" :class="{ current: item.slug === slug }" :aria-current="item.slug === slug ? 'true' : undefined" @click="switchProject(item.slug)"><span class="book-cover"><BookOpen :size="18" /></span><span><strong>{{ item.title }}</strong><small>{{ item.slug }}/</small></span><Check v-if="item.slug === slug" :size="15" /><ArrowUpRight v-else :size="15" /></button>
    </div>
    <form class="new-project" @submit.prevent="createNewProject"><label class="field-label" for="project-new-title">New project</label><div><input id="project-new-title" v-model="newProjectTitle" placeholder="An untitled novel" required maxlength="200" /><button class="primary-button" :disabled="busy"><Plus :size="16" />{{ busy ? 'Creating…' : 'Create project' }}</button></div><p v-if="dialogError" class="error-text" role="alert">{{ dialogError }}</p></form>
    <div class="dialog-actions"><button type="button" class="text-button" @click="settings">Manuscript settings</button><button type="button" class="small-button" @click="dialog = ''; workspace.leaveProject()">All projects</button></div>
  </AppDialog>
  <AppDialog v-if="dialog === 'search'" title="Find a thread" wide @close="dialog = ''"><div class="search-field"><Search :size="20" /><input v-model="search" autofocus aria-label="Search manuscript" placeholder="Search scenes, notes, and the words between…" /><Loader2 v-if="searching" class="spin" :size="18" /></div><div class="search-results"><button v-for="result in results" :key="result.document.id" @click="selectDocument(result.document); dialog = ''"><FileText :size="19" /><span><strong>{{ result.document.title }}</strong><small>{{ result.excerpt }}</small><code>{{ result.document.path }}</code></span><ArrowUpRight :size="15" /></button><p v-if="!results.length && !searching">{{ search ? 'No matching words yet. Try another phrase.' : 'Look for a character, a place, or a half-remembered sentence.' }}</p></div><p v-if="dialogError" class="error-text">{{ dialogError }}</p></AppDialog>
  <AppDialog v-if="dialog === 'settings'" title="Your workspace" @close="dialog = ''"><form @submit.prevent="saveSettings"><label class="field-label" for="project-title">Manuscript title</label><input id="project-title" v-model="projectTitle" required maxlength="200" /><label class="field-label" for="project-goal">Manuscript word goal</label><input id="project-goal" v-model.number="projectGoal" type="number" min="0" max="10000000" required /><label class="field-label" for="project-scene-goal">Default scene word goal <span>For new scenes; each scene can still set its own</span></label><input id="project-scene-goal" v-model.number="projectSceneGoal" type="number" min="0" max="10000000" required /><div class="format-note"><Feather :size="19" /><p>Scenes are ordinary Markdown files. Your synopsis, notes, and scene order travel with the project.</p></div><p v-if="dialogError" class="error-text" role="alert">{{ dialogError }}</p><div class="dialog-actions"><button v-if="passwordRequired" type="button" class="text-button" @click="logout(); dialog = ''">Lock workspace</button><button class="primary-button" :disabled="busy">Save workspace</button></div></form></AppDialog>
  <AppDialog v-if="dialog === 'history'" title="Earlier words" wide @close="dialog = ''"><p class="dialog-description">Saved revisions of {{ active?.document.title }}. Restoring a version adds it to your editor.</p><p v-if="historyNote" class="field-help">{{ historyNote }}</p><div class="history-layout"><div class="snapshot-list"><button v-for="snapshot in snapshots" :key="snapshot.id" @click="previewSnapshot(snapshot)"><Clock3 :size="16" /><span><strong>{{ date(snapshot.created) }}</strong><small>{{ number(snapshot.wordCount) }} words</small></span><ChevronRight :size="14" /></button><p v-if="!snapshots.length">{{ busy ? 'Looking through your revisions…' : 'Your revisions will appear after the first edit is saved.' }}</p></div><pre class="snapshot-preview">{{ snapshotText ?? 'Choose a revision to read it here.' }}</pre></div><p v-if="dialogError" class="error-text">{{ dialogError }}</p><div class="dialog-actions"><button class="small-button" @click="dialog = ''">Close</button><button class="primary-button" :disabled="snapshotText === null || !!active?.conflict || !!active?.saving || (!!active && workspace.dirty(active))" @click="restoreSnapshot">Restore to editor</button></div><p v-if="active && workspace.dirty(active)" class="field-help">Save the current draft before restoring a revision.</p></AppDialog>
  <AppDialog v-if="dialog === 'move'" title="Move or rename file" @close="dialog = ''"><form @submit.prevent="moveFile"><p class="dialog-description">The scene keeps its synopsis, notes, and place in the manuscript.</p><label class="field-label" for="file-path">Path within your workspace</label><input id="file-path" v-model="movedPath" required /><p class="field-help">For example: Manuscript/Chapter 02/The letter.md</p><p v-if="dialogError" class="error-text">{{ dialogError }}</p><div class="dialog-actions"><button type="button" class="small-button" @click="dialog = ''">Cancel</button><button class="primary-button" :disabled="busy || !!active?.saving">Move file</button></div></form></AppDialog>
  <AppDialog v-if="dialog === 'conflict' && active" title="Give both versions a look" wide @close="dialog = ''"><p class="dialog-description">{{ active.conflict === 'deleted' ? 'The original file is no longer here. Save your draft as a new scene or download a copy.' : 'Your draft and the file on disk have changed independently. You can edit your draft below before saving it.' }}</p><div class="conflict-columns"><div><label class="field-label" for="conflict-draft">Your browser draft</label><textarea id="conflict-draft" :value="active.content" @input="workspace.edit(($event.target as HTMLTextAreaElement).value)" /></div><div><span class="field-label">Current file on disk</span><pre>{{ active.conflict && active.conflict !== 'deleted' ? active.conflict.content : 'The file has been removed.' }}</pre></div></div><p v-if="dialogError" class="error-text">{{ dialogError }}</p><div class="conflict-actions"><button class="text-button" @click="downloadDraft"><ArrowDownToLine :size="15" />Download draft</button><button class="small-button" :disabled="busy" @click="action(async () => { await workspace.saveCopy(); dialog = '' })">Save as new scene</button><button v-if="active.conflict === 'deleted'" class="small-button" @click="workspace.discard(); dialog = ''">Let it go</button><button v-if="active.conflict !== 'deleted'" class="small-button" @click="workspace.useDisk(); dialog = ''">Use disk version</button><button v-if="active.conflict !== 'deleted'" class="primary-button" :disabled="busy" @click="action(async () => { await workspace.keepMine(); dialog = '' })">Save my version</button></div></AppDialog>
</template>
