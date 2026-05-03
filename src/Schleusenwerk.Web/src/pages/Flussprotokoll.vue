<template>
  <div>
    <div class="page-header">
      <h1>FLUSSPROTOKOLL</h1><span class="separator">——</span>
      <div class="bp-live-dot"></div>
      <span style="color: var(--bp-primary); font-family: var(--bp-font-mono); font-size: 11px;">LIVE</span>
      <div style="margin-left: auto;"><button class="bp-btn-outline" @click="events.togglePause">{{ events.paused ? '▶ Fortsetzen' : '⏸ Pausieren' }}</button></div>
    </div>
    <div class="bp-panel" style="padding: 0; overflow: hidden;">
      <div style="display: grid; grid-template-columns: 80px 180px 1fr 50px 120px; padding: 10px 14px; border-bottom: 1px solid var(--bp-border); background: rgba(30,58,95,0.3);">
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">ZEIT</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TYP</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TOR</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">STATUS</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">INFO</span>
      </div>
      <div v-for="(evt, i) in events.entries" :key="i"
           style="display: grid; grid-template-columns: 80px 180px 1fr 50px 120px; padding: 7px 14px; border-bottom: 1px solid rgba(30,58,95,0.4); align-items: center; font-family: var(--bp-font-mono); font-size: 12px;">
        <span style="color: var(--bp-border);">{{ evt.time }}</span>
        <span :style="{ color: getTypeColor(evt.type) }">{{ evt.type }}</span>
        <span style="color: var(--bp-text-primary);">{{ evt.domain }}</span>
        <span :style="{ color: evt.isHealthy ? 'var(--bp-primary)' : 'var(--bp-error)' }">{{ evt.isHealthy ? '→' : '⊧' }}</span>
        <span style="color: var(--bp-text-secondary); font-size: 11px;">{{ evt.upstream || evt.message }}</span>
      </div>
      <div v-if="events.entries.length === 0" style="padding: 24px; text-align: center; color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">Warte auf Ereignisse…</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useEventsStore } from '@/stores/events'

const events = useEventsStore()
onMounted(() => events.init())

function getTypeColor(type: string): string {
  switch (type) {
    case 'RouteUpdated': return 'var(--bp-primary)'; case 'RouteRemoved': return 'var(--bp-error)';
    case 'UpstreamHealthChanged': return 'var(--bp-warning)'; case 'CertificateProvisioned': return 'var(--bp-primary)';
    case 'CertificateExpiring': return 'var(--bp-warning)'; default: return 'var(--bp-text-secondary)'
  }
}
</script>
