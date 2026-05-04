<template>
  <div class="bp-kpi">
    <div class="bp-kpi-label">{{ label }}</div>
    <div class="bp-kpi-value" :style="{ color }">{{ value }}</div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ label: string; value: string | number; color?: string }>()
</script>
