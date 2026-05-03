<template>
  <span class="bp-badge" :class="source === 'docker' ? 'bp-badge-primary' : 'bp-badge-neutral'">
    {{ source === 'docker' ? 'docker' : 'manuell' }}
  </span>
</template>

<script setup lang="ts">
defineProps<{ source: string }>()
</script>
