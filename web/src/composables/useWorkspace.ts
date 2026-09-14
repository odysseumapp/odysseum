import { computed, reactive, ref } from 'vue'
import { FetchApiClient } from '../api/FetchApiClient'
import { OdysseumApi } from '../api/OdysseumApi'
import { ApiError, OFFLINE_MESSAGE, isOffline } from '../api/IApiClient'
import type { IOdysseumApi } from '../api/IOdysseumApi'
import type { DocumentContent, DocumentSummary, MetadataFields, Project, ProjectInfo, ProjectSettings } from '../models'
import { downloadText, exportMarkdown } from '../services/ManuscriptExport'
import { searchManuscript } from '../services/ManuscriptSearch'
import { ProjectSession } from '../services/ProjectSession'
import { WorkspaceLibrary } from '../services/WorkspaceLibrary'
import { openMirrorStore, type IMirrorStore, type MirroredDocument, type PendingEdit } from '../storage'
import type { ISyncListener, SyncStatus } from '../sync/ISyncEngine'

/** A document as shown in the editor: the server's copy (`base`/`revision`) plus whatever the writer has typed. */
export interface Buffer {
  document: DocumentSummary
  content: string
  base: string
  revision: string
  saving: boolean
  error: string
  conflict: DocumentContent | 'deleted' | null
}

// Projects are addressed as /p/<folder name>; the folder name is what the writer sees in their workspace.
function slugFromUrl() {
  const match = location.pathname.match(/^\/p\/([^/]+)/)
  try { return match ? decodeURIComponent(match[1]) : '' } catch { return '' }
}

/** Reactive state for the UI over the workspace services. Every server or storage detail lives below this. */
export function useWorkspace(api: IOdysseumApi = new OdysseumApi(new FetchApiClient())) {
  const projects = ref<ProjectInfo[]>([])
  const slug = ref('')
  const project = ref<Project | null>(null)
  const selectedId = ref('')
  const buffers = reactive(new Map<string, Buffer>())
  const active = computed(() => buffers.get(selectedId.value))
  const error = ref('')
  const notice = ref('')
  const sync = ref<SyncStatus>({ online: true, syncing: false, pending: 0, lastSync: null, error: '' })
  const connected = computed(() => sync.value.online)
  const durable = ref(true)
  const authenticated = ref(false)
  const passwordRequired = ref(false)
  const loading = ref(true)
  /** Scene details the server refused; the inspector picks these up as unsaved drafts. */
  const rejectedDetails = reactive(new Map<string, MetadataFields>())
  let mirror: IMirrorStore | undefined
  let library: WorkspaceLibrary | undefined
  let session: ProjectSession | undefined
  let listening = false
  const dirty = (buffer: Buffer) => buffer.content !== buffer.base
  const contentOf = (id: string) => buffers.get(id)?.content ?? ''
  const plain = <T>(value: T): T => JSON.parse(JSON.stringify(value))

  async function services() {
    mirror ??= await openMirrorStore()
    library ??= new WorkspaceLibrary(api, mirror)
    return { mirror, library }
  }

  function applyDocument(doc: MirroredDocument, pending: PendingEdit | undefined) {
    const existing = buffers.get(doc.id)
    // Keystrokes not yet written to the mirror are newer than anything the engine can report.
    const typing = !!existing && !!session?.engine.isWriting(doc.id) && existing.content !== doc.content
    const content = typing ? existing!.content : pending?.content ?? doc.content
    if (!existing) {
      buffers.set(doc.id, { document: doc.document, content, base: doc.content, revision: doc.document.revision, saving: false, error: '', conflict: pending?.conflict ?? null })
      return
    }
    const updatedFromServer = existing.base !== doc.content && !pending && !typing
    existing.document = doc.document
    existing.base = doc.content
    existing.revision = doc.document.revision
    existing.conflict = pending?.conflict ?? null
    if (existing.content !== content) existing.content = content
    if (updatedFromServer && doc.id === selectedId.value) notice.value = 'Updated from your files'
  }

  function selectFirst() {
    const first = project.value?.documents.find(doc => buffers.has(doc.id))
    selectedId.value = first?.id ?? ''
  }

  function rememberProject(next: string) {
    try { localStorage.setItem('odysseum:last-project', next) } catch { /* Storage may be unavailable. */ }
  }

  function listenerFor(target: () => string): ISyncListener {
    const current = () => slug.value === target()
    return {
      onProject(view) {
        if (!current()) return
        project.value = view
        for (const summary of view.documents) {
          const buffer = buffers.get(summary.id)
          if (buffer) buffer.document = summary
        }
      },
      onDocument(doc, pending) { if (current()) applyDocument(doc, pending) },
      onDocumentRenamed(from, to) {
        if (!current()) return
        const buffer = buffers.get(from)
        if (buffer) { buffers.delete(from); buffer.document = { ...buffer.document, id: to }; buffers.set(to, buffer) }
        if (selectedId.value === from) {
          selectedId.value = to
          try { localStorage.setItem(`odysseum:${project.value?.id}:last-document`, to) } catch { /* Storage may be unavailable. */ }
        }
        const rejected = rejectedDetails.get(from)
        if (rejected) { rejectedDetails.delete(from); rejectedDetails.set(to, rejected) }
      },
      onRemoved(id) {
        if (!current()) return
        buffers.delete(id)
        if (selectedId.value === id) selectFirst()
      },
      onProjectRenamed(next) {
        if (!current()) return
        slug.value = next
        setUrl(next, true)
        rememberProject(next)
      },
      onStatus(status) {
        if (!current()) return
        sync.value = status
        for (const buffer of buffers.values()) buffer.saving = status.syncing && dirty(buffer) && !buffer.conflict
      },
      onProblem(message) { if (current()) error.value = message },
      onMetadataRejected(id, fields, message) {
        if (!current()) return
        rejectedDetails.set(id, fields)
        error.value = message
      },
      isOpen: id => current() && id === selectedId.value,
    }
  }

  function edit(content: string) {
    const buffer = active.value
    if (!buffer) return
    buffer.content = content
    buffer.error = ''
    notice.value = ''
    session?.engine.edit(buffer.document.id, content)
  }

  async function open(id: string) {
    selectedId.value = id
    try { localStorage.setItem(`odysseum:${project.value?.id}:last-document`, id) } catch { /* Storage may be unavailable. */ }
    if (buffers.has(id) || !session) return
    const { mirror: m } = await services()
    const doc = await m.getDocument(session.slug, id)
    if (doc) applyDocument(doc, await m.getPending(session.slug, id))
    else await session.engine.syncNow()
  }

  /** Ctrl+S and the footer button: push what is pending right away. */
  async function save() { await session?.engine.syncNow() }
  async function refresh() { if (session) await session.engine.syncNow(); else await loadProjects() }

  async function loadProjects() {
    const { library: lib } = await services()
    const result = await lib.list()
    projects.value = result.projects
    sync.value = { ...sync.value, online: result.online }
  }

  function setUrl(next: string, replace = false) {
    const url = next ? `/p/${encodeURIComponent(next)}` : '/'
    if (location.pathname !== url) history[replace ? 'replaceState' : 'pushState'](null, '', url)
  }

  async function closeProject() {
    await session?.close()
    session = undefined
    buffers.clear()
    rejectedDetails.clear()
    selectedId.value = ''
    project.value = null
    notice.value = ''
    error.value = ''
  }

  async function openProject(next: string, replaceHistory = false) {
    await closeProject()
    slug.value = next
    setUrl(next, replaceHistory)
    loading.value = true
    try {
      const { mirror: m } = await services()
      let opened: ProjectSession
      opened = new ProjectSession(api, m, next, listenerFor(() => opened.slug))
      session = opened
      const hydrated = await opened.hydrate()
      opened.start()
      if (!hydrated) {
        await opened.engine.syncNow()
        if (!project.value) {
          error.value = sync.value.online ? sync.value.error || 'Could not open this project.'
            : "This project hasn't been opened on this device yet, so it isn't available offline."
          await opened.close()
          session = undefined
          slug.value = ''
          setUrl('', true)
          return
        }
      }
      rememberProject(next)
      let last: string | null = null
      try { last = localStorage.getItem(`odysseum:${project.value?.id}:last-document`) } catch { /* Storage may be unavailable. */ }
      const first = project.value?.documents.find(doc => doc.id === last) ?? project.value?.documents[0]
      if (first) await open(first.id)
    } finally { loading.value = false }
  }

  async function leaveProject(replaceHistory = false) {
    await closeProject()
    slug.value = ''
    setUrl('', replaceHistory)
    try { localStorage.removeItem('odysseum:last-project') } catch { /* Storage may be unavailable. */ }
    try { await loadProjects() } catch (ex) { showError(ex) }
  }

  async function createProject(title: string) {
    const { library: lib } = await services()
    const created = await lib.create(title)
    projects.value = [...projects.value, created]
    await openProject(created.slug)
    return created
  }

  function onPopState() {
    const next = slugFromUrl()
    if (next === slug.value) return
    if (next) void openProject(next, true)
    else void leaveProject(true)
  }

  async function start() {
    loading.value = true
    try {
      const { mirror: m, library: lib } = await services()
      durable.value = m.durable
      const info = await lib.session()
      if (info) {
        authenticated.value = info.authenticated
        passwordRequired.value = info.passwordRequired
      } else {
        // Offline: work from the local copy; the server asks for the password again when it is back.
        authenticated.value = true
        sync.value = { ...sync.value, online: false }
        error.value = OFFLINE_MESSAGE
      }
      if (!authenticated.value) return
      await loadProjects()
      if (!listening) { window.addEventListener('popstate', onPopState); listening = true }
      const fromUrl = slugFromUrl()
      let last = ''
      try { last = localStorage.getItem('odysseum:last-project') ?? '' } catch { /* Storage may be unavailable. */ }
      const target = fromUrl || (projects.value.some(item => item.slug === last) ? last : projects.value.length === 1 ? projects.value[0].slug : '')
      if (target) await openProject(target, true)
      else setUrl('', true)
    } catch (ex) { showError(ex) }
    finally { loading.value = false }
  }

  async function login(password: string) {
    const { library: lib } = await services()
    await lib.login(password)
  }

  async function logout() {
    const { library: lib } = await services()
    await lib.logout()
    stop()
    authenticated.value = false
  }

  async function create(title: string, folder: string, content?: string) {
    if (!session) throw new Error('Open a project first.')
    const created = await session.changes.createDocument(title, folder, content)
    await open(created.id)
    return created
  }

  async function saveDetails(id: string, fields: MetadataFields, base: MetadataFields) {
    if (!session) throw new Error('Open a project first.')
    // Hand the services plain data: Vue's reactive proxies cannot be stored in IndexedDB.
    await session.changes.updateMetadata(id, plain(fields), plain(base))
  }

  async function move(path: string) {
    if (!session || !active.value) return
    await session.changes.moveDocument(active.value.document.id, path.replace(/\\/g, '/'))
  }

  async function reorder(ids: string[]) {
    if (!session) return
    await session.changes.reorder(ids)
  }

  async function updateSettings(settings: ProjectSettings) {
    if (!session) return
    await session.changes.updateSettings(plain(settings))
  }

  function search(query: string) {
    return project.value ? searchManuscript(project.value, contentOf, query) : []
  }

  function exportManuscript() {
    if (!project.value) return
    downloadText(`${project.value.settings.title}.md`, exportMarkdown(project.value, contentOf))
  }

  async function snapshots(id: string) {
    if (!session) return { snapshots: [], fresh: false }
    return session.history.list(session.slug, id)
  }

  async function snapshot(id: string, snapshotId: string) {
    if (!session) throw new Error('Open a project first.')
    return session.history.read(session.slug, id, snapshotId)
  }

  async function useDisk() {
    const buffer = active.value
    if (!buffer || !buffer.conflict || buffer.conflict === 'deleted') return
    await session?.engine.useServer(buffer.document.id)
  }

  async function keepMine() {
    const buffer = active.value
    if (!buffer || !buffer.conflict || buffer.conflict === 'deleted') return
    await session?.engine.keepMine(buffer.document.id)
  }

  async function saveCopy() {
    const buffer = active.value
    if (!buffer) return
    const deleted = buffer.conflict === 'deleted' ? buffer.document.id : ''
    await create(`${buffer.document.title} — recovered`, buffer.document.folder, buffer.content)
    if (deleted) await session?.engine.discard(deleted)
  }

  /** Let go of text whose file was removed elsewhere. */
  async function discard() {
    const buffer = active.value
    if (!buffer || buffer.conflict !== 'deleted') return
    await session?.engine.discard(buffer.document.id)
  }

  function showError(ex: unknown) {
    error.value = ex instanceof Error ? ex.message : 'Could not reach your workspace.'
    if (ex instanceof ApiError && ex.status === 401) authenticated.value = false
    if (isOffline(ex)) sync.value = { ...sync.value, online: false }
  }

  function beforeUnload(event: BeforeUnloadEvent) {
    if (!session?.engine.hasUnwritten()) return
    void session.engine.flush()
    event.preventDefault()
  }

  function stop() {
    void session?.close()
    session = undefined
    if (listening) { window.removeEventListener('popstate', onPopState); listening = false }
  }

  return {
    projects, slug, project, selectedId, active, error, notice, sync, connected, durable, authenticated, passwordRequired, loading, rejectedDetails,
    dirty, edit, open, save, refresh, start, login, logout, create, saveDetails, move, reorder, updateSettings, search, exportManuscript,
    snapshots, snapshot, useDisk, keepMine, saveCopy, discard, showError, beforeUnload, stop, loadProjects, openProject, leaveProject, createProject,
  }
}
