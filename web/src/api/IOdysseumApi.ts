import type { DocumentContent, MetadataFields, Project, ProjectInfo, ProjectSettings, SearchResult, SessionInfo, Snapshot } from '../models'

/** Every server endpoint, typed. Nothing else in the app builds a URL. */
export interface IOdysseumApi {
  getSession(): Promise<SessionInfo>
  login(password: string): Promise<SessionInfo>
  logout(): Promise<void>

  listProjects(): Promise<ProjectInfo[]>
  createProject(title: string, wordGoal?: number): Promise<ProjectInfo>
  getProject(slug: string): Promise<Project>
  updateSettings(slug: string, settings: ProjectSettings, revision: string): Promise<Project>
  reorder(slug: string, ids: string[], revision: string): Promise<Project>
  search(slug: string, query: string): Promise<SearchResult[]>
  eventsUrl(slug: string): string

  listDocuments(slug: string): Promise<DocumentContent[]>
  getDocument(slug: string, id: string): Promise<DocumentContent>
  createDocument(slug: string, title: string, folder: string, content?: string): Promise<DocumentContent>
  saveDocument(slug: string, id: string, content: string, revision: string): Promise<DocumentContent>
  updateMetadata(slug: string, id: string, fields: MetadataFields, revision: string): Promise<Project>
  moveDocument(slug: string, id: string, path: string, revision: string): Promise<DocumentContent>
  listSnapshots(slug: string, id: string): Promise<Snapshot[]>
  getSnapshot(slug: string, id: string, snapshot: string): Promise<string>
}
