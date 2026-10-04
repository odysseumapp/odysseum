<script setup lang="ts">
import { computed, ref } from 'vue'
import IconCircleCheck from '~icons/lucide/circle-check'
import IconPlus from '~icons/lucide/plus'
import IconX from '~icons/lucide/x'
import { ui } from '../host'
import type { DocumentSummary, Link } from '../odysseum'
const props = defineProps<{ link: Link | undefined; col: DocumentSummary; name: string }>()
const emit = defineEmits<{ toggle: []; note: [note: string] }>()
const { Button, Textarea } = ui()
const note = computed(() => props.link?.note ?? '')
const editing = ref(false)
const draft = ref('')
function edit() { draft.value = note.value; editing.value = true }
function finish(keep: boolean) {
  if (!editing.value) return
  editing.value = false
  if (keep && draft.value.trim() !== note.value) emit('note', draft.value.trim())
}
</script>

<template>
  <div v-if="link" class="vw:group vw:relative vw:mx-auto vw:flex vw:flex-col vw:items-center vw:gap-1 vw:w-fit vw:max-w-full">
    <div class="vw:flex vw:items-center vw:rounded-md vw:border vw:border-primary vw:bg-default">
      <Button size="xs" color="primary" variant="ghost" :icon="IconCircleCheck" :aria-label="`Note on ${name} and ${col.title}`" :title="note ? 'Edit note' : 'Add a note'" @click="edit" />
      <Button size="xs" color="neutral" variant="ghost" :icon="IconX" class="vw:hidden vw:group-hover:flex vw:group-focus-within:flex" :aria-label="`Unlink ${name} from ${col.title}`" :title="`Unlink ${name} from ${col.title}`" @click="emit('toggle')" />
    </div>
    <Textarea
      v-if="editing" v-model="draft" autofocus autoresize :rows="2" :maxrows="8" :maxlength="2000" size="sm" class="vw:w-44"
      :aria-label="`Note on ${name} and ${col.title}`" placeholder="What connects these?"
      @blur="finish(true)" @keydown.enter.exact.prevent="finish(true)" @keydown.esc.prevent.stop="finish(false)"
    />
    <button
      v-else-if="note" type="button" class="vw:w-44 vw:rounded vw:bg-default vw:px-1 vw:text-left vw:text-xs vw:text-muted vw:hover:text-default vw:focus-visible:outline-2 vw:focus-visible:outline-primary"
      :aria-label="`Edit note on ${name} and ${col.title}`" @click="edit"
    >
      <span class="vw:line-clamp-3 vw:whitespace-pre-line vw:break-words">{{ note }}</span>
    </button>
  </div>
  <Button v-else size="xs" color="neutral" variant="outline" :icon="IconPlus" class="vw:relative vw:mx-auto vw:flex vw:bg-default" :aria-label="`Link ${name} to ${col.title}`" :title="`Link ${name} to ${col.title}`" @click="emit('toggle')" />
</template>
