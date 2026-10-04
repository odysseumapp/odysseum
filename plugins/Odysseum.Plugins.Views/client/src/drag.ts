const TYPE = 'application/x-odysseum-view-item'

export function startDrag(event: DragEvent, folderId: string, id: string) {
  event.dataTransfer?.setData(TYPE, JSON.stringify({ folderId, id }))
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}

/** The ID of the item dragged from the same folder, if any. */
export function dragged(event: DragEvent, folderId: string): string | undefined {
  try {
    const item = JSON.parse(event.dataTransfer?.getData(TYPE) ?? '')
    if (item.folderId === folderId && typeof item.id === 'string') return item.id
  } catch {  }
}
