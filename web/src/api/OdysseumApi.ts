import type { ApiCollection, DocumentContent, MetadataFields, Project, ProjectInfo, ProjectSettings, SearchResult, SessionInfo, Snapshot } from '../models'
import type { IApiClient } from './IApiClient'
import type { IOdysseumApi } from './IOdysseumApi'

export class OdysseumApi implements IOdysseumApi {
  constructor(private readonly client: IApiClient) {}

  private project = (slug: string) => `/projects/${encodeURIComponent(slug)}`
  private document = (slug: string, id: string) => `${this.project(slug)}/documents/${encodeURIComponent(id)}`

  getSession() { return this.client.request<SessionInfo>('/session') }
  login(password: string) { return this.client.request<SessionInfo>('/login', 'POST', { password }) }
  logout() { return this.client.request('/logout', 'POST') }

  async listProjects() { return (await this.client.request<ApiCollection<ProjectInfo>>('/projects')).items }
  createProject(title: string, wordGoal?: number) { return this.client.request<ProjectInfo>('/projects', 'POST', { title, wordGoal }) }
  getProject(slug: string) { return this.client.request<Project>(this.project(slug)) }
  updateSettings(slug: string, settings: ProjectSettings, revision: string) {
    return this.client.request<Project>(`${this.project(slug)}/settings`, 'PUT', { ...settings, revision })
  }
  reorder(slug: string, ids: string[], revision: string) { return this.client.request<Project>(`${this.project(slug)}/order`, 'PUT', { ids, revision }) }
  async search(slug: string, query: string) {
    return (await this.client.request<ApiCollection<SearchResult>>(`${this.project(slug)}/search?q=${encodeURIComponent(query)}`)).items
  }
  eventsUrl(slug: string) { return `/api${this.project(slug)}/events` }

  async listDocuments(slug: string) { return (await this.client.request<ApiCollection<DocumentContent>>(`${this.project(slug)}/documents`)).items }
  getDocument(slug: string, id: string) { return this.client.request<DocumentContent>(this.document(slug, id)) }
  createDocument(slug: string, title: string, folder: string, content?: string) {
    return this.client.request<DocumentContent>(`${this.project(slug)}/documents`, 'POST', { title, folder, content })
  }
  saveDocument(slug: string, id: string, content: string, revision: string) {
    return this.client.request<DocumentContent>(this.document(slug, id), 'PUT', { content, revision })
  }
  updateMetadata(slug: string, id: string, fields: MetadataFields, revision: string) {
    return this.client.request<Project>(`${this.document(slug, id)}/metadata`, 'PUT', { ...fields, revision })
  }
  moveDocument(slug: string, id: string, path: string, revision: string) {
    return this.client.request<DocumentContent>(`${this.document(slug, id)}/path`, 'PUT', { path, revision })
  }
  async listSnapshots(slug: string, id: string) { return (await this.client.request<ApiCollection<Snapshot>>(`${this.document(slug, id)}/snapshots`)).items }
  async getSnapshot(slug: string, id: string, snapshot: string) {
    return (await this.client.request<{ content: string }>(`${this.document(slug, id)}/snapshots/${encodeURIComponent(snapshot)}`)).content
  }
}
