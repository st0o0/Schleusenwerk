import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type ProxyHealthResponse } from '@/api/client'

export const useHealthStore = defineStore('health', () => {
  const data = ref<ProxyHealthResponse | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetch() {
    loading.value = true
    error.value = null
    try { data.value = await api.health.get() }
    catch (e: any) { error.value = e.message }
    finally { loading.value = false }
  }

  return { data, loading, error, fetch }
})
