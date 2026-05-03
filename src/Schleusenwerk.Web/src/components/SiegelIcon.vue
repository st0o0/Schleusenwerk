<template>
  <span :style="{ color, fontSize: '13px' }" :title="title">&#9670;</span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ status: 'valid' | 'expiring' | 'self-signed' }>()
const color = computed(() => {
  switch (props.status) { case 'valid': return 'var(--bp-primary)'; case 'expiring': return 'var(--bp-warning)'; default: return 'var(--bp-text-secondary)' }
})
const title = computed(() => {
  switch (props.status) { case 'valid': return 'Gültiges Siegel'; case 'expiring': return 'Siegel läuft bald ab'; case 'self-signed': return 'Selbstsigniert'; default: return 'Unbekannt' }
})
</script>
