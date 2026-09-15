export type DocumentStatus = 'draft' | 'revised' | 'done'
/** Derived from the document's top-level folder on the server; every kind shares the same file and metadata handling. */
export type DocumentKind = 'scene' | 'note' | 'character' | 'location' | 'arc' | 'beat'

export interface DocumentSummary {
  id: string; path: string; title: string; folder: string;
  synopsis: string; notes: string; status: DocumentStatus;
  wordGoal: number; order: number; wordCount: number; revision: string; lastModified: string;
  kind: DocumentKind; characters: string[]; locations: string[]; arcPositions: Record<string, number>;
}
export interface DocumentContent { document: DocumentSummary; content: string }
export interface ProjectSettings { title: string; wordGoal: number; defaultSceneWordGoal: number }
export interface Project { id: string; settings: ProjectSettings; revision: string; documents: DocumentSummary[]; warning: string | null }
export interface ProjectInfo { slug: string; title: string; id: string; lastModified: string }
export interface ApiCollection<T> { totalItems: number; items: T[] }
export interface Snapshot { id: string; created: string; wordCount: number }
export interface SessionInfo { authenticated: boolean; passwordRequired: boolean }
export interface SearchResult { document: DocumentSummary; excerpt: string }

/** The editable details of a scene, as shown in the inspector. */
export interface MetadataFields { title: string; synopsis: string; notes: string; status: DocumentStatus; wordGoal: number; characters: string[]; locations: string[]; arcPositions: Record<string, number> }

export const countWords = (text: string) => (text.match(/[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*/gu) ?? []).length
export const statusLabel = (status: string) => ({ draft: 'First draft', revised: 'In revision', done: 'Finished' }[status] ?? status)
