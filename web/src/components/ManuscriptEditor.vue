<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Compartment, EditorState, type Range } from '@codemirror/state'
import { Decoration, EditorView, ViewPlugin, keymap, type DecorationSet, type ViewUpdate } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab, undo, redo } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { HighlightStyle, syntaxHighlighting, syntaxTree } from '@codemirror/language'
import { searchKeymap, highlightSelectionMatches } from '@codemirror/search'
import { tags } from '@lezer/highlight'

const props = defineProps<{ modelValue: string; documentId: string; source: boolean; fontSize: number }>()
const emit = defineEmits<{ 'update:modelValue': [value: string]; save: [] }>()
const container = ref<HTMLDivElement>()
let view: EditorView | undefined
let applying = false
const presentation = new Compartment()
const appearance = new Compartment()
const syntax = HighlightStyle.define([
  { tag: tags.heading1, fontSize: '1.6em', fontWeight: '600', lineHeight: '1.4' },
  { tag: tags.heading2, fontSize: '1.35em', fontWeight: '600' },
  { tag: tags.heading, fontWeight: '600' },
  { tag: tags.strong, fontWeight: '700' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.strikethrough, textDecoration: 'line-through' },
  { tag: tags.link, color: '#3f715e', textDecoration: 'underline' },
  { tag: tags.url, color: '#6e8279', fontSize: '.85em' },
  { tag: tags.monospace, fontFamily: 'ui-monospace, monospace', fontSize: '.85em', backgroundColor: '#f0eee8' },
  { tag: tags.processingInstruction, color: '#a5aaa1' },
  { tag: tags.quote, color: '#6a7169', fontStyle: 'italic' },
])

function marks(editor: EditorView): DecorationSet {
  const ranges: Range<Decoration>[] = []
  const selectedLines = editor.state.selection.ranges.map(range => ({
    from: editor.state.doc.lineAt(range.from).from, to: editor.state.doc.lineAt(range.to).to,
  }))
  for (const visible of editor.visibleRanges) {
    syntaxTree(editor.state).iterate({ from: visible.from, to: visible.to, enter(node) {
      if (!['HeaderMark', 'EmphasisMark', 'StrikethroughMark'].includes(node.name)) return
      if (selectedLines.some(line => node.from >= line.from && node.from <= line.to)) return
      ranges.push(Decoration.replace({}).range(node.from, node.to))
    } })
  }
  return Decoration.set(ranges, true)
}
const livePreview = ViewPlugin.fromClass(class {
  decorations: DecorationSet
  constructor(editor: EditorView) { this.decorations = marks(editor) }
  update(update: ViewUpdate) {
    if (update.docChanged || update.selectionSet || update.viewportChanged || syntaxTree(update.startState) !== syntaxTree(update.state))
      this.decorations = marks(update.view)
  }
}, { decorations: plugin => plugin.decorations })

function create() {
  view?.destroy()
  const newline = props.modelValue.includes('\r\n') ? '\r\n' : '\n'
  view = new EditorView({
    parent: container.value,
    state: EditorState.create({
      doc: props.modelValue,
      extensions: [
        history(), markdown(), syntaxHighlighting(syntax), highlightSelectionMatches(), EditorView.lineWrapping,
        EditorState.lineSeparator.of(newline),
        keymap.of([
          { key: 'Mod-s', run: () => { emit('save'); return true } },
          { key: 'Mod-b', run: () => { format('bold'); return true } },
          { key: 'Mod-i', run: () => { format('italic'); return true } },
          ...defaultKeymap, ...historyKeymap, ...searchKeymap, indentWithTab,
        ]),
        EditorView.contentAttributes.of({ 'aria-label': 'Manuscript editor', role: 'textbox', 'aria-multiline': 'true', spellcheck: 'true', autocapitalize: 'sentences' }),
        presentation.of(props.source ? [] : livePreview),
        appearance.of(EditorView.theme({ '&': { fontSize: `${props.fontSize}px` } })),
        EditorView.updateListener.of(update => {
          if (update.docChanged && !applying) emit('update:modelValue', update.state.sliceDoc())
        }),
      ],
    }),
  })
}

function format(type: string) {
  if (!view) return
  if (type === 'undo') { undo(view); view.focus(); return }
  if (type === 'redo') { redo(view); view.focus(); return }
  const range = view.state.selection.main
  const selected = view.state.sliceDoc(range.from, range.to)
  const wrap = type === 'bold' ? '**' : type === 'italic' ? '*' : type === 'strike' ? '~~' : ''
  if (wrap) {
    const before = view.state.sliceDoc(Math.max(0, range.from - wrap.length), range.from)
    const after = view.state.sliceDoc(range.to, range.to + wrap.length)
    if (before === wrap && after === wrap) view.dispatch({
      changes: [{ from: range.from - wrap.length, to: range.from }, { from: range.to, to: range.to + wrap.length }],
      selection: { anchor: range.from - wrap.length, head: range.to - wrap.length },
    })
    else view.dispatch({ changes: { from: range.from, to: range.to, insert: wrap + selected + wrap }, selection: { anchor: range.from + wrap.length, head: range.to + wrap.length } })
  } else if (type === 'break') {
    view.dispatch({ changes: { from: range.from, to: range.to, insert: '\n\n---\n\n' }, selection: { anchor: range.from + 7 } })
  } else {
    const line = view.state.doc.lineAt(range.from)
    const prefix = type === 'heading' ? '## ' : type === 'quote' ? '> ' : '- '
    view.dispatch({ changes: { from: line.from, insert: prefix }, selection: { anchor: range.from + prefix.length } })
  }
  view.focus()
}
onMounted(create)
watch(() => props.documentId, create)
watch(() => props.modelValue, value => {
  if (!view || value === view.state.sliceDoc()) return
  applying = true
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } })
  applying = false
})
watch(() => props.source, value => view?.dispatch({ effects: presentation.reconfigure(value ? [] : livePreview) }))
watch(() => props.fontSize, value => view?.dispatch({ effects: appearance.reconfigure(EditorView.theme({ '&': { fontSize: `${value}px` } })) }))
onBeforeUnmount(() => view?.destroy())
defineExpose({ format, focus: () => view?.focus() })
</script>

<template><div ref="container" class="manuscript-editor" :class="{ 'show-source': source }" /></template>
