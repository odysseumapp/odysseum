<script setup lang="ts">
import { computed } from 'vue'
import { Check, GitBranch, MapPin, Plus, UserRound } from '@lucide/vue'
import type { DocumentSummary } from '../models'

const props = defineProps<{ kind: 'character' | 'location' | 'arc'; documents: DocumentSummary[]; modelValue: string[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: string[]]; create: [] }>()
const label = computed(() => props.kind === 'arc' ? 'Arcs' : props.kind === 'character' ? 'Characters' : 'Locations')
const attached = (id: string) => props.modelValue.includes(id)
const toggle = (id: string) => emit('update:modelValue', attached(id) ? props.modelValue.filter(item => item !== id) : [...props.modelValue, id])
</script>

<template>
  <div class="document-link-picker" :class="`${kind}-picker`" role="group" :aria-label="`${label} in this scene`">
    <button v-for="document in documents" :key="document.id" type="button" class="document-link-chip" :class="{ on: attached(document.id) }" :aria-pressed="attached(document.id)" @click="toggle(document.id)">
      <Check v-if="attached(document.id)" :size="12" /><UserRound v-else-if="kind === 'character'" :size="12" /><MapPin v-else-if="kind === 'location'" :size="12" /><GitBranch v-else :size="12" />{{ document.title }}
    </button>
    <button type="button" class="document-link-chip add" @click="emit('create')"><Plus :size="12" />New {{ kind }}</button>
    <p v-if="!documents.length" class="field-help">No {{ label.toLowerCase() }} yet. Add one to make it available for every scene.</p>
  </div>
</template>
