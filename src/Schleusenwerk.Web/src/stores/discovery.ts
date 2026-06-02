import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type DiscoveredContainer } from '@/api/client'

export const useDiscoveryStore = defineStore('discovery', () => {
  const containers = ref<DiscoveredContainer[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const enabled = ref(false)
  const connected = ref(false)

  async function fetchContainers() {
    loading.value = true
    error.value = null
    try {
      const result = await api.discovery.listContainers()
      enabled.value = result.enabled
      containers.value = result.containers
      connected.value = true
    } catch (e: any) {
      error.value = e.message
      connected.value = false
    } finally {
      loading.value = false
    }
  }

  return { containers, loading, error, enabled, connected, fetchContainers }
})
