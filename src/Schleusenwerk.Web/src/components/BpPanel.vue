<template>
  <div class="bp-panel">
    <div v-if="label" class="bp-panel-label">{{ label }}</div>
    <slot />
  </div>
</template>

<script setup lang="ts">
defineProps<{ label?: string }>()
</script>
