<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { X } from '@lucide/vue'
defineProps<{ title: string; wide?: boolean }>()
const emit = defineEmits<{ close: [] }>()
const element = ref<HTMLDialogElement>()
const previous = document.activeElement as HTMLElement | null
onMounted(() => element.value?.showModal())
onBeforeUnmount(() => { element.value?.close(); previous?.focus() })
</script>

<template>
  <dialog ref="element" class="dialog" :class="{ wide }" aria-labelledby="dialog-title" @cancel.prevent="emit('close')" @click="event => { if (event.target === element) emit('close') }">
    <header class="dialog-header"><h2 id="dialog-title">{{ title }}</h2><button class="icon-button" aria-label="Close dialog" @click="emit('close')"><X :size="19" /></button></header>
    <div class="dialog-body"><slot /></div>
  </dialog>
</template>
