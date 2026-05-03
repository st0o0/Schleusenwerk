import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type RouteSummary, type RouteDetail, type CommandResult } from '@/api/client'

export const useRoutesStore = defineStore('routes', () => {
  const list = ref<RouteSummary[]>([])
  const detail = ref<RouteDetail | null>(null)
  const loading = ref(false)

  async function fetchList() {
    loading.value = true
    try { list.value = await api.routes.list() }
    finally { loading.value = false }
  }

  async function fetchDetail(domain: string) {
    loading.value = true
    try { detail.value = await api.routes.get(domain) }
    finally { loading.value = false }
  }

  async function addRoute(body: { domain: string; forceHttps?: boolean; timeoutSeconds?: number; firstUpstreamUrl?: string }): Promise<CommandResult> {
    return api.routes.add(body)
  }

  async function updateRoute(domain: string, body: { forceHttps: boolean; timeoutSeconds: number }): Promise<CommandResult> {
    return api.routes.update(domain, body)
  }

  async function deleteRoute(domain: string): Promise<CommandResult> {
    const result = await api.routes.delete(domain)
    if (result.success) { list.value = list.value.filter(r => r.domain !== domain) }
    return result
  }

  async function addUpstream(domain: string, body: { url: string; weight?: number }): Promise<CommandResult> {
    return api.routes.addUpstream(domain, body)
  }

  async function removeUpstream(domain: string, url: string): Promise<CommandResult> {
    return api.routes.removeUpstream(domain, url)
  }

  return { list, detail, loading, fetchList, fetchDetail, addRoute, updateRoute, deleteRoute, addUpstream, removeUpstream }
})
