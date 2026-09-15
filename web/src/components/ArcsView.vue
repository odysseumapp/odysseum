<script setup lang="ts">
import { computed, ref } from 'vue'
import { ArrowLeft, ArrowRight, ArrowUpRight, GitBranch, Plus, X } from '@lucide/vue'
import type { DocumentSummary } from '../models'
import AppDialog from './AppDialog.vue'

const props = defineProps<{ arcs: DocumentSummary[]; beats: DocumentSummary[]; busy: boolean }>()
const emit = defineEmits<{
  createBeat: [arc: string, title: string, position: number];
  create: []; open: [document: DocumentSummary];
  place: [document: DocumentSummary, arc: string, position: number | null];
}>()
const step = 190
const adding = ref<DocumentSummary | null>(null)
const chosen = ref('')
const newBeat = ref(true)
const beatTitle = ref('')
const position = ref(1)
const dragged = ref<{ id: string; arc: string } | null>(null)
const positions = (doc: DocumentSummary) => doc.arcPositions ?? {}
const members = (arc: string) => props.beats.filter(doc => positions(doc)[arc] !== undefined)
  .sort((a, b) => positions(a)[arc]! - positions(b)[arc]! || a.id.localeCompare(b.id))
const available = computed(() => props.beats.filter(doc => adding.value && positions(doc)[adding.value.id] === undefined))
const columns = computed(() => Math.max(5, ...props.beats.flatMap(doc => Object.values(positions(doc)).map(value => Math.ceil(value) + 2))))
const width = computed(() => columns.value * step)
// Stack coincident points from external metadata without hiding either document.
function points(arc: string) {
  const ends: number[] = []
  return members(arc).map(document => {
    const at = positions(document)[arc]!
    let row = ends.findIndex(end => end <= at)
    if (row < 0) row = ends.length
    ends[row] = at + 1
    return { document, at, row }
  })
}
const lanes = computed(() => props.arcs.map(arc => {
  const items = points(arc.id)
  return { arc, items, height: 162 + Math.max(0, ...items.map(item => item.row)) * 116 }
}))
function add(arc: DocumentSummary) {
  chosen.value = ''
  beatTitle.value = ''
  newBeat.value = true
  position.value = Math.min(10001, Math.floor(Math.max(-1, ...members(arc.id).map(doc => positions(doc)[arc.id]!))) + 2)
  adding.value = arc
}
function attach() {
  if (!adding.value || !Number.isFinite(position.value) || position.value < 1 || position.value > 10001) return
  if (newBeat.value) {
    if (!beatTitle.value.trim()) return
    emit('createBeat', adding.value.id, beatTitle.value.trim(), position.value - 1)
  } else {
    const document = props.beats.find(doc => doc.id === chosen.value)
    if (!document) return
    emit('place', document, adding.value.id, position.value - 1)
  }
  adding.value = null
}
function drop(event: DragEvent, arc: string) {
  event.preventDefault()
  if (!dragged.value || dragged.value.arc !== arc || props.busy) return
  const document = props.beats.find(doc => doc.id === dragged.value!.id)
  const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect()
  const at = Math.max(0, Math.min(10000, Math.round((event.clientX - bounds.left - 20) / step)))
  if (document) emit('place', document, arc, at)
  dragged.value = null
}
function drag(event: DragEvent, document: DocumentSummary, arc: string) {
  dragged.value = { id: document.id, arc }
  event.dataTransfer?.setData('text/plain', document.id)
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}
</script>

<template>
  <div class="arcs-view">
    <header class="arcs-heading">
      <div><span class="eyebrow">THREADS THROUGH YOUR STORY</span><h1>Story arcs</h1><p>Give each thread its own line. Place beats where they belong.</p></div>
      <button class="primary-button" @click="emit('create')"><Plus :size="16" />New arc</button>
    </header>
    <div v-if="!arcs.length" class="arcs-empty"><GitBranch :size="34" /><h2>Every story has its threads.</h2><p>Add an arc for a theme, a relationship, or a journey.<br />Then write beats along its line.</p></div>
    <template v-else>
      <div class="arcs-key"><span>{{ arcs.length }} {{ arcs.length === 1 ? 'arc' : 'arcs' }}</span><span>Independent positions · Drag points to arrange them</span></div>
      <div class="arcs-scroll" tabindex="0" role="region" aria-label="Story arc timelines">
        <div class="arcs-ruler" :style="{ width: `${width + 176}px` }"><span class="arcs-ruler-label">YOUR ARCS</span><span class="arcs-ruler-track">BEGINNING <span>DEVELOPMENT <ArrowRight :size="13" /></span></span></div>
        <section v-for="(lane, index) in lanes" :key="lane.arc.id" class="arc-lane" :aria-label="`${lane.arc.title} arc`" :style="{ width: `${width + 176}px`, '--arc-color': ['#78845b', '#a97b5f', '#77959a', '#a086a0', '#a99a61'][index % 5] }">
          <div class="arc-label"><button class="arc-name" :title="`Edit ${lane.arc.title}`" @click="emit('open', lane.arc)">{{ lane.arc.title }}<ArrowUpRight :size="12" /></button><span>{{ lane.items.length }} {{ lane.items.length === 1 ? 'beat' : 'beats' }}</span><button class="arc-add" :aria-label="`Add beat to ${lane.arc.title}`" :disabled="busy" @click="add(lane.arc)"><Plus :size="13" />Add beat</button></div>
          <div class="arc-track" :class="{ 'accepts-drop': dragged?.arc === lane.arc.id }" :style="{ width: `${width}px`, height: `${lane.height}px` }" @dragover.prevent @drop="drop($event, lane.arc.id)">
            <div class="arc-line" />
            <p v-if="!lane.items.length" class="arc-empty">A thread waiting for its first moment.</p>
            <div v-for="point in lane.items" :key="point.document.id" class="arc-point" :data-document-id="point.document.id" :data-position="point.at" :style="{ left: `${point.at * step + 20}px`, top: `${35 + point.row * 116}px` }" :draggable="!busy" @dragstart="drag($event, point.document, lane.arc.id)" @dragend="dragged = null">
              <span class="arc-dot" />
              <div class="arc-card"><button class="arc-document" :aria-label="`Open ${point.document.title}`" @click="emit('open', point.document)"><small>POSITION {{ Number((point.at + 1).toFixed(2)) }}</small><strong>{{ point.document.title }}</strong></button><div class="arc-point-actions"><button class="icon-button" :aria-label="`Move ${point.document.title} left on ${lane.arc.title}`" :disabled="busy || point.at <= 0" @click="emit('place', point.document, lane.arc.id, Math.max(0, point.at - 1))"><ArrowLeft :size="12" /></button><button class="icon-button" :aria-label="`Move ${point.document.title} right on ${lane.arc.title}`" :disabled="busy || point.at >= 10000" @click="emit('place', point.document, lane.arc.id, Math.min(10000, point.at + 1))"><ArrowRight :size="12" /></button><button class="icon-button arc-remove" :aria-label="`Remove ${point.document.title} from ${lane.arc.title}`" :disabled="busy" @click="emit('place', point.document, lane.arc.id, null)"><X :size="12" /></button></div></div>
            </div>
          </div>
        </section>
      </div>
      <p class="arcs-footnote">A beat can belong to several arcs. Each line keeps its own arrangement.</p>
    </template>
    <AppDialog v-if="adding" :title="`Add beat to ${adding.title}`" @close="adding = null">
      <form @submit.prevent="attach">
        <div class="beat-mode"><button type="button" class="small-button" :aria-pressed="newBeat" @click="newBeat = true">New beat</button><button type="button" class="small-button" :aria-pressed="!newBeat" @click="newBeat = false">Existing beat</button></div>
        <template v-if="newBeat"><label class="field-label" for="beat-title">Beat title</label><input id="beat-title" v-model="beatTitle" placeholder="A promise is broken" maxlength="200" required /><p class="field-help">A beat is a document you can open and write in.</p></template>
        <template v-else><label class="field-label" for="arc-beat">Beat</label><select id="arc-beat" v-model="chosen" required><option disabled value="">Choose a beat</option><option v-for="beat in available" :key="beat.id" :value="beat.id">{{ beat.title }} ? {{ beat.folder }}</option></select><p v-if="!available.length" class="field-help">No other beats available. Create a new beat for this arc.</p></template>
        <label class="field-label" for="arc-position">Position</label><input id="arc-position" v-model.number="position" type="number" min="1" max="10001" step="1" required />
        <div class="dialog-actions"><button type="button" class="small-button" @click="adding = null">Cancel</button><button class="primary-button" :disabled="busy || (newBeat ? !beatTitle.trim() : !chosen)">{{ newBeat ? 'Create beat' : 'Add to arc' }}</button></div>
      </form>
    </AppDialog>
  </div>
</template>

<style scoped>
.beat-mode{display:flex;gap:8px;margin-bottom:18px}.beat-mode button[aria-pressed=true]{background:#e8eedf;border-color:#a7b595;color:#405b39}
.arcs-view{flex:1;min-height:0;overflow:auto;padding:32px 36px;background:#fafbf7}.arcs-heading{display:flex;align-items:center;justify-content:space-between;gap:20px;margin-bottom:30px}.arcs-heading h1{font-family:Georgia,serif;font-weight:400;font-size:34px;margin:9px 0}.arcs-heading p,.arcs-empty p{font-size:13px;color:#87917d;line-height:1.8}.arcs-heading .primary-button{flex-shrink:0}.arcs-key{display:flex;justify-content:space-between;font-size:11px;color:#929b88;margin-bottom:14px;gap:15px}.arcs-key>span:first-child{font-weight:600;color:#68775a}.arcs-scroll{overflow:auto;border:1px solid #e1e6d9;border-radius:10px;background:#fffefa;max-height:calc(100vh - 315px)}.arcs-ruler{display:flex;height:38px;border-bottom:1px solid #e7ebdf;background:#f5f7ef;font-size:9px;letter-spacing:1.4px;color:#98a28b;align-items:center}.arcs-ruler-label{box-sizing:border-box;width:176px;flex-shrink:0;position:sticky;left:0;z-index:3;background:#f5f7ef;padding:12px 20px}.arcs-ruler-track{display:flex;padding:0 20px;gap:95px;align-items:center}.arcs-ruler-track>span{display:flex;gap:12px;align-items:center}.arc-lane{display:flex;border-bottom:1px solid #edf0e6}.arc-lane:last-child{border-bottom:0}.arc-label{position:sticky;left:0;z-index:2;width:176px;box-sizing:border-box;padding:30px 18px;flex-shrink:0;background:#fffefa;border-right:1px solid #edf0e6;display:flex;align-items:flex-start;flex-direction:column;gap:9px}.arc-name{display:flex;text-align:left;gap:6px;align-items:center;color:var(--arc-color);font-family:Georgia,serif;font-size:20px;overflow-wrap:anywhere;background:none;border:0;padding:0}.arc-name svg{flex-shrink:0}.arc-label>span{font-size:10px;color:#9ca48f}.arc-add{display:flex;align-items:center;gap:4px;margin-top:11px;font-size:10px;color:#7e8a6e;background:none;border:0;padding:0}.arc-track{position:relative;flex-shrink:0;background:repeating-linear-gradient(to right,transparent,transparent 189px,#f2f4ed 189px,#f2f4ed 190px)}.arc-line{position:absolute;left:20px;right:25px;top:42px;height:2px;background:var(--arc-color);opacity:.42}.arc-line:after{content:'';position:absolute;right:0;top:-3px;width:7px;height:7px;border-right:2px solid var(--arc-color);border-top:2px solid var(--arc-color);transform:rotate(45deg)}.arc-point{position:absolute;width:160px}.arc-dot{display:block;height:12px;width:12px;border:3px solid #fffefa;box-shadow:0 0 0 1px var(--arc-color);background:var(--arc-color);border-radius:50%;box-sizing:content-box}.arc-card{margin-top:9px;border:1px solid #e1e6d7;border-left:3px solid var(--arc-color);border-radius:5px;background:#fffefa;box-shadow:0 2px 5px #26371308}.arc-document{display:block;width:100%;padding:9px 10px 5px;text-align:left;background:none;border:0}.arc-document small{display:block;font-size:8px;color:#9ca48f;letter-spacing:1px;margin-bottom:4px}.arc-document strong{display:block;font-size:12px;font-weight:500;color:#536246;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.arc-point-actions{display:flex;padding:0 5px 3px;gap:0}.arc-point-actions .icon-button{width:24px;height:23px;color:#98a18d}.arc-point-actions .arc-remove{margin-left:auto}.arc-card:hover,.arc-card:focus-within{border-color:var(--arc-color)}.arc-point[draggable=true]{cursor:grab}.accepts-drop{background-color:#f3f6ed}.arc-empty{position:absolute;left:20px;top:61px;font-size:12px;font-style:italic;color:#a3ab96}.arcs-footnote{font-size:11px;color:#9ca48f;margin-top:15px}.arcs-empty{padding:65px 20px;text-align:center;color:#a1ad91}.arcs-empty h2{font-family:Georgia,serif;font-weight:400;color:#69765b;font-size:25px}.arcs-empty svg{margin-bottom:10px}
@media(max-width:760px){.arcs-view{padding:24px 16px}.arcs-heading{align-items:flex-start}.arcs-heading h1{font-size:28px}.arcs-heading p{font-size:12px}.arcs-key{flex-direction:column;gap:5px}.arcs-scroll{max-height:calc(100vh - 345px)}}
</style>
