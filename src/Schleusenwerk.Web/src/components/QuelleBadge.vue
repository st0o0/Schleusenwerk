<template>
  <span class="bp-badge" :class="source === 'docker' ? 'bp-badge-primary' : 'bp-badge-neutral'">
    {{ source === 'docker' ? t('source.docker') : t('source.manual') }}
  </span>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
defineProps<{ source: string }>()
</script>
