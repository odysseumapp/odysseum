/**
 * The types of Odysseum's client plugin API, version 1. This is a copy of `odysseum-web/app/plugin-host/contract.ts`
 * with the models it uses; a third-party plugin can copy this file as it is.
 */
import type { Component } from 'vue'

export type DocumentStatus = 'draft' | 'revised' | 'done'
export type DocumentKind = 'scene' | 'note' | 'character' | 'location' | 'thread' | 'style'
export type ViewSettings = Record<string, unknown>

export interface ProjectInfo {
  id: string; name: string; title: string; wordGoal: number; defaultSceneWordGoal: number;
  rootFolderId: string; lastModified: string; warning: string | null; etag: string;
}
export interface Folder {
  id: string; projectId: string; name: string; parentFolderId: string | null; childIds: string[];
  ownDocumentId: string | null; pinnedView: string | null; views: Record<string, ViewSettings>; etag: string;
}
export interface DocumentSummary {
  id: string; projectId: string; folderId: string; name: string; kind: DocumentKind; isFolderDocument: boolean;
  title: string; synopsis: string; notes: string; status: DocumentStatus; wordGoal: number; wordCount: number;
  lastModified: string; etag: string;
}
export interface DocumentDetails { title: string; synopsis: string; notes: string; status: DocumentStatus; wordGoal: number }
export interface Link { id: string; projectId: string; firstDocumentId: string; secondDocumentId: string; note: string; etag: string }
export interface ProjectSnapshot { project: ProjectInfo; folders: Folder[]; documents: DocumentSummary[]; links: Link[] }

/** One child of a folder, in the folder's order. A folder's own document is never one of them. */
export type ViewItem =
  | { type: 'folder'; id: string; title: string; folder: Folder; document?: never }
  | { type: 'document'; id: string; title: string; document: DocumentSummary; folder?: never }

/** The props the host gives a view component. All of them are reactive and read-only. */
export interface ViewProps {
  folder: Folder
  items: ViewItem[]
  settings: ViewSettings
  project: ProjectSnapshot
}

/** The events a view sends. The host puts each change in the offline queue. */
export interface ViewEmits {
  open: [itemId: string]
  move: [itemId: string, targetFolderId: string, index: number]
  saveSettings: [settings: ViewSettings | null]
  updateDetails: [documentId: string, patch: Partial<DocumentDetails>]
  link: [firstDocumentId: string, secondDocumentId: string]
  unlink: [linkId: string]
  setLinkNote: [linkId: string, note: string]
  createDocument: [folderId: string]
}

export interface ViewRegistration { name: string; label: string; icon: Component; component: Component }

export interface ComponentKit {
  Button: Component
  Icon: Component
  Badge: Component
  Select: Component
  Textarea: Component
  ItemCard: Component
}

export interface OdysseumPluginApi {
  readonly apiVersion: 1
  readonly vue: typeof import('vue')
  readonly ui: ComponentKit
  registerView(view: ViewRegistration): void
}
