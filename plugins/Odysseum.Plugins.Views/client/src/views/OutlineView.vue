<script setup lang="ts">
import type { ViewEmits, ViewProps } from '../odysseum'
import CollectionView from './CollectionView.vue'
defineProps<ViewProps>()
const emit = defineEmits<ViewEmits>()
</script>

<template>
  <CollectionView :folder="folder" :items="items" :compact="true" @open="id => emit('open', id)" @move="(id, target, index) => emit('move', id, target, index)" />
</template>
