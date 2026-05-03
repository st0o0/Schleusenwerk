<template>
  <span class="bp-status-dot" :class="status" :title="title"></span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const props = defineProps<{ status: 'offen' | 'gesperrt' | 'warnung' | 'neutral' }>()
const title = computed(() => {
  switch (props.status) { case 'offen': return t('status.healthy'); case 'gesperrt': return t('status.unhealthy'); case 'warnung': return t('status.warning'); default: return props.status }
})
</script>
