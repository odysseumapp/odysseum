import type { ProjectInfo } from '../models'
import { renameInOp, type CachedSnapshots, type IMirrorStore, type MirroredDocument, type MirroredProject, type PendingEdit, type PendingOp } from './IMirrorStore'

export const DB_NAME = 'odysseum'
export const DB_VERSION = 2

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

function complete(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
    tx.onabort = () => reject(tx.error)
  })
}

export function upgrade(db: IDBDatabase) {
  const names = db.objectStoreNames
  if (!names.contains('meta')) db.createObjectStore('meta', { keyPath: 'key' })
  if (!names.contains('projects')) db.createObjectStore('projects', { keyPath: 'slug' })
  if (!names.contains('documents')) db.createObjectStore('documents', { keyPath: ['slug', 'id'] }).createIndex('slug', 'slug')
  if (!names.contains('pending')) db.createObjectStore('pending', { keyPath: ['slug', 'id'] }).createIndex('slug', 'slug')
  if (!names.contains('ops')) db.createObjectStore('ops', { keyPath: 'seq', autoIncrement: true }).createIndex('slug', 'slug')
  if (!names.contains('snapshots')) db.createObjectStore('snapshots', { keyPath: ['slug', 'id'] }).createIndex('slug', 'slug')
}

export class IndexedDbMirrorStore implements IMirrorStore {
  readonly durable = true
  constructor(private readonly db: IDBDatabase) {}

  private read<T>(store: string, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
    return request(run(this.db.transaction(store, 'readonly').objectStore(store)))
  }

  private async write(stores: string | string[], run: (tx: IDBTransaction) => void): Promise<void> {
    const tx = this.db.transaction(stores, 'readwrite')
    run(tx)
    await complete(tx)
  }

  async listProjects() {
    const row = await this.read<{ key: string; projects: ProjectInfo[] } | undefined>('meta', s => s.get('projects'))
    return row?.projects ?? []
  }
  putProjects(projects: ProjectInfo[]) { return this.write('meta', tx => tx.objectStore('meta').put({ key: 'projects', projects })) }
  getProject(slug: string) { return this.read<MirroredProject | undefined>('projects', s => s.get(slug)) }
  putProject(project: MirroredProject) { return this.write('projects', tx => tx.objectStore('projects').put(project)) }

  async renameProject(from: string, to: string) {
    const [project, documents, pending, ops, snapshots] = await Promise.all([
      this.getProject(from), this.listDocuments(from), this.listPending(from), this.listOps(from),
      this.read<CachedSnapshots[]>('snapshots', s => s.index('slug').getAll(from)),
    ])
    await this.write(['projects', 'documents', 'pending', 'ops', 'snapshots'], tx => {
      if (project) { tx.objectStore('projects').delete(from); tx.objectStore('projects').put({ ...project, slug: to }) }
      for (const doc of documents) { tx.objectStore('documents').delete([from, doc.id]); tx.objectStore('documents').put({ ...doc, slug: to }) }
      for (const edit of pending) { tx.objectStore('pending').delete([from, edit.id]); tx.objectStore('pending').put({ ...edit, slug: to }) }
      for (const op of ops) tx.objectStore('ops').put({ ...op, slug: to })
      for (const cached of snapshots) { tx.objectStore('snapshots').delete([from, cached.id]); tx.objectStore('snapshots').put({ ...cached, slug: to }) }
    })
  }

  listDocuments(slug: string) { return this.read<MirroredDocument[]>('documents', s => s.index('slug').getAll(slug)) }
  getDocument(slug: string, id: string) { return this.read<MirroredDocument | undefined>('documents', s => s.get([slug, id])) }
  putDocuments(documents: MirroredDocument[]) {
    return documents.length ? this.write('documents', tx => { for (const doc of documents) tx.objectStore('documents').put(doc) }) : Promise.resolve()
  }
  deleteDocument(slug: string, id: string) { return this.write('documents', tx => tx.objectStore('documents').delete([slug, id])) }

  async renameDocument(slug: string, from: string, to: string) {
    const [doc, edit, ops, cached] = await Promise.all([this.getDocument(slug, from), this.getPending(slug, from), this.listOps(slug), this.getSnapshots(slug, from)])
    await this.write(['documents', 'pending', 'ops', 'snapshots'], tx => {
      if (doc) { tx.objectStore('documents').delete([slug, from]); tx.objectStore('documents').put({ ...doc, id: to, document: { ...doc.document, id: to } }) }
      if (edit) { tx.objectStore('pending').delete([slug, from]); tx.objectStore('pending').put({ ...edit, id: to }) }
      for (const op of ops) {
        const renamed = renameInOp(op.op, from, to)
        if (renamed !== op.op) tx.objectStore('ops').put({ ...op, op: renamed })
      }
      if (cached) { tx.objectStore('snapshots').delete([slug, from]); tx.objectStore('snapshots').put({ ...cached, id: to }) }
    })
  }

  listPending(slug: string) { return this.read<PendingEdit[]>('pending', s => s.index('slug').getAll(slug)) }
  getPending(slug: string, id: string) { return this.read<PendingEdit | undefined>('pending', s => s.get([slug, id])) }
  putPending(edit: PendingEdit) { return this.write('pending', tx => tx.objectStore('pending').put(edit)) }
  deletePending(slug: string, id: string) { return this.write('pending', tx => tx.objectStore('pending').delete([slug, id])) }

  async listOps(slug: string) {
    const ops = await this.read<PendingOp[]>('ops', s => s.index('slug').getAll(slug))
    return ops.sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0))
  }
  async putOp(op: PendingOp) {
    const tx = this.db.transaction('ops', 'readwrite')
    const seq = await request(tx.objectStore('ops').put(op))
    await complete(tx)
    return { ...op, seq: seq as number }
  }
  deleteOp(seq: number) { return this.write('ops', tx => tx.objectStore('ops').delete(seq)) }

  getSnapshots(slug: string, id: string) { return this.read<CachedSnapshots | undefined>('snapshots', s => s.get([slug, id])) }
  putSnapshots(snapshots: CachedSnapshots) { return this.write('snapshots', tx => tx.objectStore('snapshots').put(snapshots)) }
}
