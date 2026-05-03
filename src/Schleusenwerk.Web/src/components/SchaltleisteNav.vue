<template>
  <nav class="schaltleiste">
    <div class="schaltleiste-logo">
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
        <rect x="2" y="6" width="20" height="12" rx="2" stroke="#64ffda" stroke-width="1.5" fill="none" />
        <line x1="12" y1="6" x2="12" y2="18" stroke="#64ffda" stroke-width="1.5" stroke-dasharray="3 2" />
      </svg>
      <span>{{ t('nav.brand') }}</span>
    </div>
    <div class="schaltleiste-tabs">
      <RouterLink to="/" class="schaltleiste-tab" exact>{{ t('nav.dashboard') }}</RouterLink>
      <RouterLink to="/tore" class="schaltleiste-tab">{{ t('nav.routes') }}</RouterLink>
      <RouterLink to="/siegel" class="schaltleiste-tab">{{ t('nav.certificates') }}</RouterLink>
      <RouterLink to="/flussprotokoll" class="schaltleiste-tab">{{ t('nav.eventLog') }}</RouterLink>
      <RouterLink to="/hafenbecken" class="schaltleiste-tab">{{ t('nav.discovery') }}</RouterLink>
      <RouterLink to="/stellwerk" class="schaltleiste-tab">{{ t('nav.settings') }}</RouterLink>
    </div>
    <div class="schaltleiste-status">
      <button class="locale-toggle" @click="toggleLocale">{{ locale === 'de' ? 'EN' : 'DE' }}</button>
      <div class="dot" :class="{ error: !systemOk }"></div>
      <span>{{ systemOk ? t('nav.systemOk') : t('nav.systemError') }}</span>
    </div>
  </nav>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useHealthStore } from '@/stores/health'
import { setLocale } from '@/i18n'
import type { SupportedLocale } from '@/i18n'

const { t, locale } = useI18n()
const health = useHealthStore()
const systemOk = computed(() => health.data?.unhealthyCount === 0)

function toggleLocale() {
  const next: SupportedLocale = locale.value === 'de' ? 'en' : 'de'
  setLocale(next)
}
</script>
