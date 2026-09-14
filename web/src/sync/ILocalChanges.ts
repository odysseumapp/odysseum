import type { DocumentSummary, MetadataFields, ProjectSettings } from '../models'

/**
 * Records every change other than typing. Each is applied to the local view at once and queued for the
 * server, so the app behaves the same whether or not the server is reachable.
 */
export interface ILocalChanges {
  createDocument(title: string, folder: string, content?: string): Promise<DocumentSummary>
  updateMetadata(id: string, fields: MetadataFields, base: MetadataFields): Promise<void>
  moveDocument(id: string, path: string): Promise<void>
  reorder(ids: string[]): Promise<void>
  updateSettings(settings: ProjectSettings): Promise<void>
}
