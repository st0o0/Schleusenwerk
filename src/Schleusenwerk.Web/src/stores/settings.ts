import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type ProxySettings, type CommandResult } from '@/api/client'

export const useSettingsStore = defineStore('settings', () => {
  const data = ref<ProxySettings | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetch() {
    loading.value = true
    error.value = null
    try { data.value = await api.settings.get() }
    catch (e: any) { error.value = e.message }
    finally { loading.value = false }
  }

  async function update(body: Partial<ProxySettings>): Promise<CommandResult> {
    return api.settings.update(body)
  }

  return { data, loading, error, fetch, update }
})
