<template>
  <div>
    <div class="page-header"><h1>{{ t('certificates.title') }}</h1><span class="separator">——</span><span class="subtitle">{{ t('certificates.subtitle') }}</span></div>
    <div v-if="certs.loading" style="color: var(--bp-text-secondary);">{{ t('common.loading') }}</div>
    <table v-else class="bp-table">
      <thead><tr><th style="width: 28px;"></th><th>{{ t('common.domain') }}</th><th>{{ t('certificates.thumbprint') }}</th><th>{{ t('certificates.validUntil') }}</th><th>{{ t('certificates.type') }}</th><th style="width: 80px;"></th><th style="width: 80px;"></th></tr></thead>
      <tbody>
        <tr v-for="cert in certs.list" :key="cert.domain">
          <td><SiegelIcon :status="getSiegelStatus(cert)" /></td>
          <td style="color: var(--bp-text-primary);">{{ cert.domain }}</td>
          <td style="font-size: 11px; color: var(--bp-text-secondary);">{{ cert.thumbprint.slice(0, 12) }}…</td>
          <td><span :style="{ color: isExpiring(cert) ? 'var(--bp-warning)' : 'var(--bp-primary)' }">{{ formatDate(cert.notAfter) }}</span></td>
          <td><span class="bp-badge" :class="cert.isSelfSigned ? 'bp-badge-warning' : 'bp-badge-primary'">{{ cert.isSelfSigned ? t('certificates.selfSigned') : t('certificates.acme') }}</span></td>
          <td><button class="bp-btn-outline" style="font-size: 11px; padding: 2px 8px;" @click="renew(cert.domain)">{{ t('certificates.renew') }}</button></td>
          <td>
            <label class="bp-btn-outline" style="font-size: 11px; padding: 2px 8px; cursor: pointer;">
              {{ t('certificates.upload') }}
              <input type="file" style="display: none;" @change="(e) => handleUpload(cert.domain, e)" accept=".pem,.crt,.pfx,.p12" />
            </label>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import SiegelIcon from '@/components/SiegelIcon.vue'
import { useCertificatesStore } from '@/stores/certificates'
import type { CertificateSummary } from '@/api/client'

const { t } = useI18n()
const certs = useCertificatesStore()
onMounted(() => certs.fetchList())

function isExpiring(cert: CertificateSummary): boolean { return new Date(cert.notAfter).getTime() - Date.now() < 14 * 24 * 60 * 60 * 1000 }
function getSiegelStatus(cert: CertificateSummary): 'valid' | 'expiring' | 'self-signed' {
  if (cert.isSelfSigned) { return 'self-signed' }
  if (isExpiring(cert)) { return 'expiring' }
  return 'valid'
}
function formatDate(iso: string): string { try { return new Date(iso).toISOString().slice(0, 10) } catch { return iso } }
async function renew(domain: string) { await certs.provision(domain) }
async function handleUpload(domain: string, event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files?.length) { return }
  const file = input.files[0]
  const result = await certs.upload(domain, file)
  if (result.success) { await certs.fetchList() }
  input.value = ''
}
</script>
