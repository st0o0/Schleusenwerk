<template>
  <div>
    <div class="bp-breadcrumb">
      <RouterLink to="/tore">{{ t('routes.title') }}</RouterLink>
      <span style="color: var(--bp-border); margin: 0 6px;">/</span>
      <span class="current">{{ domain }}</span>
    </div>
    <div v-if="routes.loading" style="color: var(--bp-text-secondary);">{{ t('common.loading') }}</div>
    <div v-else-if="!routes.detail" style="color: var(--bp-error);">{{ t('routes.notFound') }}</div>
    <template v-else>
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <StatusIndicator :status="isHealthy ? 'offen' : 'gesperrt'" />
        <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 18px; font-weight: 700;">{{ domain }}</span>
        <span class="bp-badge" :class="isHealthy ? 'bp-badge-primary' : 'bp-badge-error'">{{ isHealthy ? t('routes.healthy') : t('routes.unhealthy') }}</span>
        <span v-if="routes.detail.forceHttps" class="bp-badge bp-badge-primary">{{ t('routes.sealed') }}</span>
      </div>
      <div style="display: grid; grid-template-columns: 280px 1fr; gap: 24px;">
        <BpPanel :label="t('routes.configuration')">
          <div style="margin-bottom: 12px; display: flex; align-items: center; gap: 8px;">
            <input type="checkbox" v-model="forceHttps" />
            <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">{{ t('routes.forceHttps') }}</span>
          </div>
          <div style="margin-bottom: 16px;">
            <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">{{ t('routes.timeout') }}</label>
            <input v-model.number="timeoutSeconds" type="number" min="5" max="300" class="bp-input" />
          </div>
          <button class="bp-btn-filled" style="width: 100%;" @click="saveConfig">{{ t('common.save') }}</button>
        </BpPanel>
        <BpPanel :label="t('routes.upstreams')">
          <div style="display: flex; flex-direction: column; gap: 8px; margin-bottom: 14px;">
            <div v-for="upstream in routes.detail.upstreams" :key="upstream.url"
                 style="display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid rgba(30,58,95,0.8); border-radius: 4px;">
              <StatusIndicator :status="isUpstreamHealthy(upstream.url) ? 'offen' : 'gesperrt'" />
              <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 12px; flex: 1;">{{ upstream.url }}</span>
              <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">w:{{ upstream.weight }}</span>
              <button style="background: none; border: none; color: var(--bp-error); cursor: pointer;" @click="handleRemoveUpstream(upstream.url)">✕</button>
            </div>
          </div>
          <div style="display: flex; gap: 8px;">
            <input v-model="newUpstreamUrl" class="bp-input" placeholder="http://upstream:port" style="flex: 1;" />
            <button class="bp-btn-outline" @click="handleAddUpstream">{{ t('routes.addUpstream') }}</button>
          </div>
        </BpPanel>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusIndicator from '@/components/StatusIndicator.vue'
import BpPanel from '@/components/BpPanel.vue'
import { useRoutesStore } from '@/stores/routes'

const { t } = useI18n()
const props = defineProps<{ domain: string }>()
const routes = useRoutesStore()
const forceHttps = ref(false); const timeoutSeconds = ref(30); const newUpstreamUrl = ref('')
const isHealthy = computed(() => routes.detail?.health.every(h => h.isHealthy) ?? true)
function isUpstreamHealthy(url: string): boolean { return routes.detail?.health.find(h => h.url === url)?.isHealthy ?? true }

onMounted(async () => {
  await routes.fetchDetail(props.domain)
  if (routes.detail) { forceHttps.value = routes.detail.forceHttps; timeoutSeconds.value = routes.detail.timeoutSeconds || 30 }
})

async function saveConfig() { await routes.updateRoute(props.domain, { forceHttps: forceHttps.value, timeoutSeconds: timeoutSeconds.value }) }
async function handleAddUpstream() {
  if (!newUpstreamUrl.value) { return }
  const result = await routes.addUpstream(props.domain, { url: newUpstreamUrl.value })
  if (result.success) { newUpstreamUrl.value = ''; await routes.fetchDetail(props.domain) }
}
async function handleRemoveUpstream(url: string) {
  const result = await routes.removeUpstream(props.domain, url)
  if (result.success) { await routes.fetchDetail(props.domain) }
}
</script>
