import type { Project } from '../models'
import { isStoryNote } from './FileNames'

/** The manuscript as one Markdown file, in order, story notes excluded; the same shape the server exports. */
export function exportMarkdown(project: Project, content: (id: string) => string) {
  const scenes = project.documents.filter(doc => !isStoryNote(doc.path))
  return `# ${project.settings.title}\n\n` + scenes.map(doc => `## ${doc.title}\n\n${content(doc.id).trim()}`).join('\n\n---\n\n') + '\n'
}

/** Hands a text file to the browser's download flow. */
export function downloadText(name: string, text: string, type = 'text/markdown;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([text], { type }))
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = name
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
