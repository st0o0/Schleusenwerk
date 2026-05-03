<template>
  <span :style="{ color, fontSize: '13px' }" :title="title">&#9670;</span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const props = defineProps<{ status: 'valid' | 'expiring' | 'self-signed' }>()
const color = computed(() => {
  switch (props.status) { case 'valid': return 'var(--bp-primary)'; case 'expiring': return 'var(--bp-warning)'; default: return 'var(--bp-text-secondary)' }
})
const title = computed(() => {
  switch (props.status) { case 'valid': return t('cert.valid'); case 'expiring': return t('cert.expiring'); case 'self-signed': return t('cert.selfSigned'); default: return t('cert.unknown') }
})
</script>
