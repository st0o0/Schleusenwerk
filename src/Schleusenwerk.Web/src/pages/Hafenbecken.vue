<template>
  <div>
    <div class="page-header">
      <h1>{{ t('discovery.title') }}</h1>
      <span class="separator">——</span>
      <span class="subtitle">{{ t('discovery.subtitle') }}</span>
      <div style="margin-left: auto; display: flex; align-items: center; gap: 6px;">
        <div :class="discovery.connected ? 'bp-live-dot' : 'bp-status-dot gesperrt'" style="width: 7px; height: 7px;"></div>
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">
          {{ discovery.connected ? t('discovery.connected') : t('discovery.disconnected') }}
        </span>
      </div>
    </div>

    <div v-if="discovery.loading" style="color: var(--bp-text-secondary);">{{ t('common.loading') }}</div>
    <div v-else-if="discovery.error" style="color: var(--bp-error);">{{ discovery.error }}</div>
    <template v-else>
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
        <div v-for="container in discovery.containers" :key="container.name"
             class="bp-panel" :style="getContainerStyle(container)">
          <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 10px;">
            <StatusIndicator :status="getContainerStatus(container)" />
            <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 13px; font-weight: 600;">
              {{ container.name }}
            </span>
            <span :style="{ color: getStatusColor(container), fontFamily: 'var(--bp-font-mono)', fontSize: '10px', marginLeft: 'auto' }">
              {{ getStatusLabel(container) }}
            </span>
          </div>
          <div style="font-family: var(--bp-font-mono); font-size: 11px; color: var(--bp-text-secondary); line-height: 1.8;">
            <div>{{ t('discovery.image') }} <span style="color: var(--bp-text-primary);">{{ container.image }}</span></div>
            <template v-if="hasLabels(container)">
              <div>{{ t('discovery.labels') }}</div>
              <div v-for="(value, key) in schleusenLabels(container)" :key="key" style="margin-left: 12px;">
                <span style="color: var(--bp-primary);">{{ key }}</span>=<span style="color: var(--bp-text-primary);">{{ value }}</span>
              </div>
            </template>
            <div v-else style="color: var(--bp-border);">{{ t('discovery.noLabels') }}</div>
          </div>
          <div v-if="container.assignedDomain" style="margin-top: 10px; display: flex; align-items: center; gap: 6px; font-size: 11px;">
            <span style="color: var(--bp-primary);">→</span>
            <span style="color: var(--bp-text-secondary);">{{ t('discovery.assignedTo') }}</span>
            <RouterLink :to="`/tore/${container.assignedDomain}`"
                        style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); text-decoration: underline; text-decoration-color: var(--bp-border);">
              {{ container.assignedDomain }}
            </RouterLink>
          </div>
          <div v-if="container.conflictReason" style="margin-top: 10px; padding: 6px 10px; background: rgba(240,192,0,0.06); border: 1px solid rgba(240,192,0,0.15); border-radius: 4px;">
            <span style="color: var(--bp-warning); font-size: 11px;">{{ container.conflictReason }}</span>
          </div>
        </div>
      </div>
      <div v-if="discovery.containers.length === 0" class="bp-panel" style="text-align: center;">
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">{{ t('discovery.noContainers') }}</span>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusIndicator from '@/components/StatusIndicator.vue'
import { useDiscoveryStore } from '@/stores/discovery'
import type { DiscoveredContainer } from '@/api/client'

const { t } = useI18n()
const discovery = useDiscoveryStore()
onMounted(() => discovery.fetchContainers())

function hasLabels(c: DiscoveredContainer): boolean {
  return Object.keys(c.labels).some(k => k.startsWith('schleusenwerk.'))
}
function schleusenLabels(c: DiscoveredContainer): Record<string, string> {
  return Object.fromEntries(Object.entries(c.labels).filter(([k]) => k.startsWith('schleusenwerk.')))
}
function getContainerStatus(c: DiscoveredContainer): 'offen' | 'gesperrt' | 'warnung' | 'neutral' {
  if (c.conflictReason) { return 'warnung' }
  if (!hasLabels(c)) { return 'neutral' }
  return 'offen'
}
function getContainerStyle(c: DiscoveredContainer): string {
  if (c.conflictReason) { return 'border-color: rgba(240,192,0,0.3);' }
  if (!hasLabels(c)) { return 'opacity: 0.6;' }
  return ''
}
function getStatusColor(c: DiscoveredContainer): string {
  if (c.conflictReason) { return 'var(--bp-warning)' }
  if (!hasLabels(c)) { return 'var(--bp-text-secondary)' }
  return 'var(--bp-primary)'
}
function getStatusLabel(c: DiscoveredContainer): string {
  if (c.conflictReason) { return t('discovery.conflict') }
  if (!hasLabels(c)) { return t('discovery.noLabel') }
  return c.status
}
</script>
