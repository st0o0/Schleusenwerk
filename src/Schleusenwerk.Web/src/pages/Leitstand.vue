<template>
  <div>
    <div class="page-header">
      <h1>LEITSTAND</h1>
      <span class="separator">——</span>
      <span class="subtitle">Systemübersicht</span>
    </div>
    <div v-if="health.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <div v-else-if="health.error" style="color: var(--bp-error);">{{ health.error }}</div>
    <template v-else-if="health.data">
      <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px;">
        <BpKpi label="Tore gesamt" :value="health.data.routeCount" color="var(--bp-text-primary)" />
        <BpKpi label="Tore offen" :value="health.data.healthyCount" color="var(--bp-primary)" />
        <BpKpi label="Tore gesperrt" :value="health.data.unhealthyCount" color="var(--bp-error)" />
        <BpKpi label="Siegel ablaufend" :value="expiringCerts" color="var(--bp-warning)" />
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import BpKpi from '@/components/BpKpi.vue'
import { useHealthStore } from '@/stores/health'
import { useCertificatesStore } from '@/stores/certificates'

const health = useHealthStore()
const certs = useCertificatesStore()
const expiringCerts = ref(0)

onMounted(async () => {
  await Promise.all([health.fetch(), certs.fetchList()])
  const now = Date.now()
  const fourteenDays = 14 * 24 * 60 * 60 * 1000
  expiringCerts.value = certs.list.filter(c => new Date(c.notAfter).getTime() - now < fourteenDays).length
})
</script>
