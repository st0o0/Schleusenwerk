import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type DiscoveredContainer } from '@/api/client'

export const useDiscoveryStore = defineStore('discovery', () => {
  const containers = ref<DiscoveredContainer[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const connected = ref(true)

  async function fetchContainers() {
    loading.value = true
    error.value = null
    try {
      containers.value = await api.discovery.listContainers()
      connected.value = true
    } catch (e: any) {
      error.value = e.message
      connected.value = false
    } finally {
      loading.value = false
    }
  }

  return { containers, loading, error, connected, fetchContainers }
})
