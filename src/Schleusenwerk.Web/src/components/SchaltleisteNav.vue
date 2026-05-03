<template>
  <nav class="schaltleiste">
    <div class="schaltleiste-logo">
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
        <rect x="2" y="6" width="20" height="12" rx="2" stroke="#64ffda" stroke-width="1.5" fill="none" />
        <line x1="12" y1="6" x2="12" y2="18" stroke="#64ffda" stroke-width="1.5" stroke-dasharray="3 2" />
      </svg>
      <span>SCHLEUSENWERK</span>
    </div>
    <div class="schaltleiste-tabs">
      <RouterLink to="/" class="schaltleiste-tab" exact>LEITSTAND</RouterLink>
      <RouterLink to="/tore" class="schaltleiste-tab">SCHLEUSENTORE</RouterLink>
      <RouterLink to="/siegel" class="schaltleiste-tab">SIEGEL</RouterLink>
      <RouterLink to="/flussprotokoll" class="schaltleiste-tab">FLUSSPROTOKOLL</RouterLink>
      <RouterLink to="/hafenbecken" class="schaltleiste-tab">HAFENBECKEN</RouterLink>
      <RouterLink to="/stellwerk" class="schaltleiste-tab">STELLWERK</RouterLink>
    </div>
    <div class="schaltleiste-status">
      <div class="dot" :class="{ error: !systemOk }"></div>
      <span>{{ systemOk ? 'SYSTEM OK' : 'STÖRUNG' }}</span>
    </div>
  </nav>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useHealthStore } from '@/stores/health'

const health = useHealthStore()
const systemOk = computed(() => health.data?.unhealthyCount === 0)

onMounted(() => health.fetch())
</script>
