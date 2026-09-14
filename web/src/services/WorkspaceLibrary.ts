import type { IOdysseumApi } from '../api/IOdysseumApi'
import { isOffline } from '../api/IApiClient'
import type { ProjectInfo, SessionInfo } from '../models'
import type { IMirrorStore } from '../storage'
import { projectSlugFor } from './FileNames'

/** The list of projects and the creation of new ones; works from the local copy when the server is away. */
export class WorkspaceLibrary {
  constructor(private readonly api: IOdysseumApi, private readonly mirror: IMirrorStore) {}

  /** Null when the server cannot be reached; the caller decides whether the local copy is enough to proceed. */
  async session(): Promise<SessionInfo | null> {
    try { return await this.api.getSession() }
    catch (ex) { if (isOffline(ex)) return null; throw ex }
  }

  login(password: string) { return this.api.login(password) }
  logout() { return this.api.logout() }

  /** Server list merged with projects created here that the server has not seen yet. */
  async list(): Promise<{ projects: ProjectInfo[]; online: boolean }> {
    let projects: ProjectInfo[]
    let online: boolean
    try {
      projects = await this.api.listProjects()
      online = true
    } catch (ex) {
      if (!isOffline(ex)) throw ex
      projects = await this.mirror.listProjects()
      online = false
    }
    if (online) {
      const known = new Set(projects.map(project => project.slug))
      for (const local of await this.mirror.listProjects()) {
        if (!known.has(local.slug) && (await this.mirror.listOps(local.slug)).some(pending => pending.op.type === 'createProject')) projects.push(local)
      }
      await this.mirror.putProjects(projects)
    }
    return { projects, online }
  }

  /** Creates the project on this device first; the folder is created on the server when the project syncs. */
  async create(title: string): Promise<ProjectInfo> {
    const clean = title.trim()
    const existing = await this.mirror.listProjects()
    const slug = projectSlugFor(clean, new Set(existing.map(project => project.slug.toLowerCase())))
    const settings = { title: clean, wordGoal: 50000, defaultSceneWordGoal: 1000 }
    const info: ProjectInfo = { slug, title: clean, id: crypto.randomUUID(), lastModified: new Date().toISOString() }
    await this.mirror.putProject({ slug, project: { id: info.id, settings, revision: '', documents: [], warning: null }, syncedAt: '' })
    await this.mirror.putOp({ slug, op: { type: 'createProject', title: clean, settings }, updated: info.lastModified })
    await this.mirror.putProjects([...existing, info])
    return info
  }
}
