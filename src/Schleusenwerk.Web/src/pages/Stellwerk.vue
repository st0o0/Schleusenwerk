<template>
  <div>
    <div class="page-header">
      <h1>{{ t('settings.title') }}</h1>
      <span class="separator">——</span>
      <span class="subtitle">{{ t('settings.subtitle') }}</span>
    </div>

    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 24px;">
      <BpPanel :label="t('settings.acmeConfig')">
        <div style="margin-bottom: 12px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">{{ t('settings.stage') }}</label>
          <select v-model="stage" class="bp-input">
            <option value="local">{{ t('settings.stageLocal') }}</option>
            <option value="staging">{{ t('settings.stageStaging') }}</option>
            <option value="production">{{ t('settings.stageProduction') }}</option>
          </select>
        </div>
        <div style="margin-bottom: 12px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">{{ t('settings.contactEmail') }}</label>
          <input v-model="acmeEmail" class="bp-input" placeholder="admin@example.com" />
        </div>
        <div style="margin-bottom: 16px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">{{ t('settings.dnsProvider') }}</label>
          <input v-model="dnsProvider" class="bp-input" placeholder="cloudflare, route53, hetzner, ..." />
        </div>
        <button class="bp-btn-filled" @click="saveSettings" :disabled="saving">
          {{ saving ? t('common.saving') : t('common.save') }}
        </button>
        <p v-if="saveMessage" style="color: var(--bp-primary); font-size: 11px; margin-top: 8px;">{{ saveMessage }}</p>
      </BpPanel>

      <BpPanel :label="t('settings.systemInfo')">
        <div style="font-family: var(--bp-font-mono); font-size: 12px; line-height: 2.2;">
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">{{ t('settings.version') }}</span>
            <span>0.1.0</span>
          </div>
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">{{ t('settings.routeCount') }}</span>
            <span style="color: var(--bp-primary);">{{ health.data?.routeCount ?? '—' }}</span>
          </div>
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">{{ t('settings.statusLabel') }}</span>
            <span :style="{ color: isHealthy ? 'var(--bp-primary)' : 'var(--bp-error)' }">
              {{ isHealthy ? t('settings.allHealthy') : t('settings.hasErrors') }}
            </span>
          </div>
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">{{ t('settings.stage') }}</span>
            <span class="bp-badge" :class="stageBadgeClass">{{ settingsStore.data?.stage ?? '—' }}</span>
          </div>
          <div v-if="settingsStore.data?.dnsProvider" style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">{{ t('settings.dnsProvider') }}</span>
            <span style="color: var(--bp-primary);">{{ settingsStore.data.dnsProvider }}</span>
          </div>
        </div>
      </BpPanel>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import BpPanel from '@/components/BpPanel.vue'
import { useHealthStore } from '@/stores/health'
import { useSettingsStore } from '@/stores/settings'

const { t } = useI18n()
const health = useHealthStore()
const settingsStore = useSettingsStore()

const stage = ref('local')
const acmeEmail = ref('')
const dnsProvider = ref('')
const saving = ref(false)
const saveMessage = ref('')

const isHealthy = computed(() => health.data?.unhealthyCount === 0)
const stageBadgeClass = computed(() => {
  switch (settingsStore.data?.stage) {
    case 'production': return 'bp-badge-primary'
    case 'staging': return 'bp-badge-warning'
    default: return 'bp-badge-neutral'
  }
})

onMounted(async () => {
  await Promise.all([health.fetch(), settingsStore.fetch()])
  if (settingsStore.data) {
    stage.value = settingsStore.data.stage
    acmeEmail.value = settingsStore.data.acmeEmail
    dnsProvider.value = settingsStore.data.dnsProvider
  }
})

async function saveSettings() {
  saving.value = true
  saveMessage.value = ''
  try {
    const result = await settingsStore.update({
      stage: stage.value,
      acmeEmail: acmeEmail.value,
      dnsProvider: dnsProvider.value,
    })
    if (result.success) {
      saveMessage.value = t('common.saved')
      await settingsStore.fetch()
    } else {
      saveMessage.value = result.errorMessage ?? t('common.error')
    }
  } finally {
    saving.value = false
  }
}
</script>
