<template>
  <span class="bp-status-dot" :class="status" :title="title"></span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ status: 'offen' | 'gesperrt' | 'warnung' | 'neutral' }>()
const title = computed(() => {
  switch (props.status) { case 'offen': return 'Offen'; case 'gesperrt': return 'Gesperrt'; case 'warnung': return 'Warnung'; default: return props.status }
})
</script>
