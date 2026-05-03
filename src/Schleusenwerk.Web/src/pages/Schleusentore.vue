<template>
  <div>
    <div class="page-header">
      <h1>{{ t('routes.title') }}</h1>
      <span class="separator">——</span>
      <span class="subtitle">{{ t('routes.configured', { count: routes.list.length }) }}</span>
      <div style="margin-left: auto;">
        <RouterLink to="/tore/neu" class="bp-btn-outline">{{ t('routes.addRoute') }}</RouterLink>
      </div>
    </div>
    <div v-if="routes.loading" style="color: var(--bp-text-secondary);">{{ t('common.loading') }}</div>
    <table v-else class="bp-table">
      <thead><tr><th style="width: 28px;"></th><th>{{ t('common.domain') }}</th><th>{{ t('routes.source') }}</th><th>{{ t('routes.upstreams') }}</th><th>{{ t('routes.certificate') }}</th><th style="width: 60px;"></th></tr></thead>
      <tbody>
        <tr v-for="route in routes.list" :key="route.domain">
          <td><StatusIndicator status="offen" /></td>
          <td><RouterLink :to="`/tore/${route.domain}`" style="color: var(--bp-text-primary);">{{ route.domain }}</RouterLink></td>
          <td><QuelleBadge :source="route.source" /></td>
          <td style="text-align: center;">{{ route.upstreams.length }}</td>
          <td style="text-align: center;"><SiegelIcon :status="getCertStatus(route.domain)" /></td>
          <td><button style="background: none; border: none; color: var(--bp-error); cursor: pointer; font-size: 16px;" @click="handleDelete(route.domain)">✕</button></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusIndicator from '@/components/StatusIndicator.vue'
import QuelleBadge from '@/components/QuelleBadge.vue'
import SiegelIcon from '@/components/SiegelIcon.vue'
import { useRoutesStore } from '@/stores/routes'
import { useCertificatesStore } from '@/stores/certificates'

const { t } = useI18n()
const routes = useRoutesStore()
const certs = useCertificatesStore()
const certStatus = ref<Record<string, 'valid' | 'expiring' | 'self-signed'>>({})

onMounted(async () => {
  await Promise.all([routes.fetchList(), certs.fetchList()])
  const now = Date.now()
  const fourteenDays = 14 * 24 * 60 * 60 * 1000
  for (const cert of certs.list) {
    if (cert.isSelfSigned) { certStatus.value[cert.domain] = 'self-signed' }
    else if (new Date(cert.notAfter).getTime() - now < fourteenDays) { certStatus.value[cert.domain] = 'expiring' }
    else { certStatus.value[cert.domain] = 'valid' }
  }
})

function getCertStatus(domain: string): 'valid' | 'expiring' | 'self-signed' { return certStatus.value[domain] ?? 'valid' }
async function handleDelete(domain: string) { await routes.deleteRoute(domain) }
</script>
