/** TODO: Find a way to stop mirroring these */

import type { DocumentKind } from '../models'

/** Mirrors the server's file-name rules so a scene or project created offline lands on the same path later. */
export function fileName(title: string) {
  let result = title.replace(/[<>:"/\\|?*\x00-\x1f]/g, '-').replace(/^[ .-]+|[ .-]+$/g, '')
  if (!result) result = 'Untitled'
  if (/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)/i.test(result)) result = 'Scene-' + result
  return result
}

/** Same rules as the server: unique file name within the folder, `-2`, `-3`, … on collision. */
export function pathFor(title: string, folder: string, taken: Set<string>) {
  const stem = fileName(title)
  const dir = folder.trim().replace(/^\/+|\/+$/g, '')
  const make = (name: string) => dir ? `${dir}/${name}.md` : `${name}.md`
  let path = make(stem)
  for (let suffix = 2; taken.has(path.toLowerCase()); suffix++) path = make(`${stem}-${suffix}`)
  return path
}

/** A folder name for a new project, unique among those already known. */
export function projectSlugFor(title: string, taken: Set<string>) {
  const stem = fileName(title)
  let slug = stem
  for (let suffix = 2; taken.has(slug.toLowerCase()); suffix++) slug = `${stem}-${suffix}`
  return slug
}

/** The server's rule for what a document is, mirrored so documents created offline are classified the same way. */
export function kindFor(path: string): DocumentKind {
  const top = path.split('/')[0].toLowerCase()
  if (top === 'characters') return 'character'
  if (top === 'locations') return 'location'
  if (top === 'arcs') return 'arc'
  if (top === 'beats') return 'beat'
  if (top === 'notes' || top === 'research' || top === 'story notes') return 'note'
  return 'scene'
}

export const isStoryNote = (path: string) => kindFor(path) !== 'scene'
