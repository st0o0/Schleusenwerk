<template>
  <div>
    <div class="page-header"><h1>STELLWERK</h1><span class="separator">——</span><span class="subtitle">Systemkonfiguration</span></div>
    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 24px;">
      <BpPanel label="ACME-Konfiguration">
        <div style="margin-bottom: 12px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Kontakt-E-Mail</label>
          <input class="bp-input" placeholder="admin@example.com" disabled />
        </div>
        <div style="margin-bottom: 16px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Anbieter</label>
          <input class="bp-input" value="Let's Encrypt" disabled />
        </div>
        <button class="bp-btn-filled" disabled>SPEICHERN</button>
        <p style="color: var(--bp-text-secondary); font-size: 11px; margin-top: 8px;">Konfiguration wird über appsettings.json verwaltet</p>
      </BpPanel>
      <BpPanel label="Systeminformation">
        <div style="font-family: var(--bp-font-mono); font-size: 12px; line-height: 2.2;">
          <div style="display: flex; justify-content: space-between;"><span style="color: var(--bp-text-secondary);">Version</span><span>0.1.0</span></div>
          <div style="display: flex; justify-content: space-between;"><span style="color: var(--bp-text-secondary);">Tore</span><span style="color: var(--bp-primary);">{{ health.data?.routeCount ?? '—' }}</span></div>
          <div style="display: flex; justify-content: space-between;"><span style="color: var(--bp-text-secondary);">Status</span><span :style="{ color: isHealthy ? 'var(--bp-primary)' : 'var(--bp-error)' }">{{ isHealthy ? 'Alle Systeme offen' : 'Störungen vorhanden' }}</span></div>
        </div>
      </BpPanel>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, computed } from 'vue'
import BpPanel from '@/components/BpPanel.vue'
import { useHealthStore } from '@/stores/health'

const health = useHealthStore()
const isHealthy = computed(() => health.data?.unhealthyCount === 0)
onMounted(() => health.fetch())
</script>
