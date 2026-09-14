import type { Project, SearchResult } from '../models'

/** Prose, title, synopsis, and notes, case-insensitive: the same rule the server applies, run over the local copy. */
export function searchManuscript(project: Project, content: (id: string) => string, query: string): SearchResult[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return []
  const results: SearchResult[] = []
  for (const document of project.documents) {
    const body = content(document.id)
    const position = body.toLowerCase().indexOf(needle)
    if (position < 0 && ![document.title, document.synopsis, document.notes].some(text => text.toLowerCase().includes(needle))) continue
    const start = Math.max(0, position - 55)
    results.push({ document, excerpt: body.substring(start, start + 180).replace(/\n/g, ' ') })
    if (results.length === 50) break
  }
  return results
}
