<script setup lang="ts">
// The Odysseum mark: a ship under sail. Drawn to sit inside .brand-mark; the hull and mainsail take the current color.
withDefaults(defineProps<{ size?: number }>(), { size: 22 })
</script>

<template>
  <svg :width="size" :height="size" viewBox="0 0 64 64" aria-hidden="true" focusable="false">
    <path d="M34 9v30" stroke="currentColor" stroke-width="3.5" stroke-linecap="round" />
    <path d="M37 13c8 5 13 13 15 24H37z" fill="currentColor" />
    <path d="M31 19c-7 4-11 11-12 18h12z" fill="#e6d98a" />
    <path d="M11 43h42l-5 9H18z" fill="currentColor" />
    <path d="M8 57c3-3 6-3 9 0s6 3 9 0 6-3 9 0 6 3 9 0 6-3 9 0" fill="none" stroke="#b9c7a7" stroke-width="3" stroke-linecap="round" />
  </svg>
</template>
