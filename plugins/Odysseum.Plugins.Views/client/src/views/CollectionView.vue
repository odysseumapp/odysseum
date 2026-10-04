<script setup lang="ts">
import IconArrowDown from '~icons/lucide/arrow-down'
import IconArrowUp from '~icons/lucide/arrow-up'
import { dragged, startDrag } from '../drag'
import { ui } from '../host'
import type { Folder, ViewItem } from '../odysseum'
const props = defineProps<{ folder: Folder; items: ViewItem[]; compact: boolean }>()
const emit = defineEmits<{ open: [itemId: string]; move: [itemId: string, targetFolderId: string, index: number] }>()
const { Button, ItemCard } = ui()
/** Puts the item where `to` is now. */
function reorder(from: string, to: string) {
  const index = props.folder.childIds.indexOf(to)
  if (from !== to && index >= 0) emit('move', from, props.folder.id, index)
}
function drop(event: DragEvent, to: string) {
  const from = dragged(event, props.folder.id)
  if (from) reorder(from, to)
}
</script>

<template>
  <div :class="compact ? 'vw:space-y-2' : 'vw:grid vw:gap-4 vw:sm:grid-cols-2 vw:xl:grid-cols-3 vw:2xl:grid-cols-4'" :aria-label="compact ? 'Outline' : 'Corkboard'">
    <div v-for="(item, index) in items" :key="item.id" :data-item-id="item.id" draggable="true" class="vw:cursor-grab" @dragstart="startDrag($event, folder.id, item.id)" @dragover.prevent @drop.prevent="drop($event, item.id)">
      <ItemCard :item="item" :compact="compact" @open="emit('open', item.id)">
        <div class="vw:flex vw:items-center vw:gap-1 vw:mt-2">
          <Button size="xs" color="neutral" variant="ghost" :icon="IconArrowUp" :disabled="index === 0" :aria-label="`Move ${item.title} earlier`" @click="reorder(item.id, items[index - 1]!.id)" />
          <Button size="xs" color="neutral" variant="ghost" :icon="IconArrowDown" :disabled="index === items.length - 1" :aria-label="`Move ${item.title} later`" @click="reorder(item.id, items[index + 1]!.id)" />
          <span v-if="item.document" class="vw:text-xs vw:text-muted vw:ml-auto">{{ item.document.status }}</span>
        </div>
      </ItemCard>
    </div>
    <p v-if="!items.length" class="vw:text-muted vw:py-8">This folder is empty. Add a document or subfolder.</p>
  </div>
</template>
