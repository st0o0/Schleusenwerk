import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type CertificateSummary, type CommandResult } from '@/api/client'

export const useCertificatesStore = defineStore('certificates', () => {
  const list = ref<CertificateSummary[]>([])
  const loading = ref(false)

  async function fetchList() {
    loading.value = true
    try { list.value = await api.certificates.list() }
    finally { loading.value = false }
  }

  async function provision(domain: string): Promise<CommandResult> {
    return api.certificates.provision(domain)
  }

  async function upload(domain: string, file: File, keyFile?: File, password?: string): Promise<CommandResult> {
    return api.certificates.upload(domain, file, keyFile, password)
  }

  return { list, loading, fetchList, provision, upload }
})
