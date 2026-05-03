<template>
  <div>
    <div class="bp-breadcrumb">
      <RouterLink to="/tore">SCHLEUSENTORE</RouterLink>
      <span style="color: var(--bp-border); margin: 0 6px;">/</span>
      <span class="current">NEU</span>
    </div>
    <div class="page-header"><h1>TOR EINSETZEN</h1></div>
    <div class="bp-panel" style="max-width: 600px;">
      <div style="margin-bottom: 16px;">
        <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Domain</label>
        <input v-model="domain" class="bp-input" placeholder="example.com" />
      </div>
      <div style="margin-bottom: 16px;">
        <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Erste Kammer</label>
        <input v-model="firstUpstreamUrl" class="bp-input" placeholder="http://backend:8080" />
      </div>
      <div style="margin-bottom: 16px; display: flex; align-items: center; gap: 8px;">
        <input type="checkbox" v-model="forceHttps" />
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">Versiegelt (HTTPS)</span>
      </div>
      <div style="display: flex; gap: 12px; margin-top: 16px;">
        <button class="bp-btn-filled" @click="submit" :disabled="submitting">{{ submitting ? 'Wird eingesetzt...' : 'TOR EINSETZEN' }}</button>
        <RouterLink to="/tore" style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px; padding: 8px 16px;">Abbrechen</RouterLink>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useRoutesStore } from '@/stores/routes'

const router = useRouter()
const routes = useRoutesStore()
const domain = ref(''); const firstUpstreamUrl = ref(''); const forceHttps = ref(false); const submitting = ref(false)

async function submit() {
  if (!domain.value) { return }
  submitting.value = true
  try {
    const result = await routes.addRoute({ domain: domain.value, forceHttps: forceHttps.value, timeoutSeconds: 30, firstUpstreamUrl: firstUpstreamUrl.value || undefined })
    if (result.success) { router.push('/tore') }
  } finally { submitting.value = false }
}
</script>
