import type { DocumentSummary, Folder, ProjectSnapshot, ViewItem } from './odysseum'

export const findFolder = (project: ProjectSnapshot, id: string | null | undefined) => id ? project.folders.find(folder => folder.id === id) : undefined
const findDocument = (project: ProjectSnapshot, id: string | null | undefined) => id ? project.documents.find(doc => doc.id === id) : undefined

export function folderChain(project: ProjectSnapshot, folderId: string): Folder[] {
  const chain: Folder[] = []
  for (let folder = findFolder(project, folderId); folder && !chain.includes(folder); folder = findFolder(project, folder.parentFolderId)) chain.unshift(folder)
  return chain
}
export const folderPath = (project: ProjectSnapshot, folderId: string) => folderChain(project, folderId).slice(1).map(folder => folder.name).join('/')
export const isInside = (project: ProjectSnapshot, folderId: string, ancestorId: string) => folderChain(project, folderId).some(folder => folder.id === ancestorId)

export function folderItems(project: ProjectSnapshot, folderId: string): ViewItem[] {
  return (findFolder(project, folderId)?.childIds ?? []).flatMap((id): ViewItem[] => {
    const child = findFolder(project, id)
    if (child) return [{ type: 'folder', id, title: child.name, folder: child }]
    const doc = findDocument(project, id)
    return doc && !doc.isFolderDocument ? [{ type: 'document', id, title: doc.title, document: doc }] : []
  })
}
export function descendantDocuments(project: ProjectSnapshot, folderId: string): DocumentSummary[] {
  return folderItems(project, folderId).flatMap(item => item.document ? [item.document] : descendantDocuments(project, item.folder.id))
}
export const folderDocument = (project: ProjectSnapshot, folder: Folder) => findDocument(project, folder.ownDocumentId)
export const linkBetween = (project: ProjectSnapshot, a: string, b: string) =>
  project.links.find(link => (link.firstDocumentId === a && link.secondDocumentId === b) || (link.firstDocumentId === b && link.secondDocumentId === a))
export const documentItem = (doc: DocumentSummary): ViewItem => ({ type: 'document', id: doc.id, title: doc.title, document: doc })
