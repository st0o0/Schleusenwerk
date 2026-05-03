const API_URL = import.meta.env.VITE_API_URL ?? ''
const BASE = `${API_URL}/api`

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok && res.status === 404) {
    throw new Error('Not found')
  }
  return res.json()
}

export interface CommandResult { success: boolean; errorMessage?: string | null }
export interface RouteSummary { domain: string; forceHttps: boolean; source: string; timeoutSeconds: number; tlsMode: string; upstreams: UpstreamInfo[] }
export interface UpstreamInfo { url: string; weight: number }
export interface RouteDetail { domain: string; forceHttps: boolean; timeoutSeconds: number; source: string; tlsMode: string; upstreams: UpstreamInfo[]; health: UpstreamHealthEntry[] }
export interface UpstreamHealthEntry { url: string; isHealthy: boolean }
export interface CertificateSummary { domain: string; thumbprint: string; notAfter: string; isSelfSigned: boolean }
export interface CertificateDetail { domain: string; thumbprint: string; notBefore: string; notAfter: string; issuer: string; isSelfSigned: boolean }
export interface ProxyHealthResponse { routeCount: number; healthyCount: number; unhealthyCount: number }
export interface UpstreamHealthResponse { domain: string; upstreams: UpstreamHealthEntry[] }
export interface ProxyEventDto { type: string; domain: string; message: string; isHealthy: boolean; upstreamUrl: string }
export interface DiscoveredContainer { name: string; image: string; status: string; labels: Record<string, string>; assignedDomain: string | null; conflictReason: string | null }
export interface ProxySettings { stage: string; acmeEmail: string; dnsProvider: string; defaultRequestTimeoutSeconds: number; maxConnectionsPerUpstream: number; forceHttpsGlobally: boolean }

function toBase64Url(input: string): string {
  return btoa(input).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

export const api = {
  routes: {
    list: () => request<RouteSummary[]>('/routes'),
    get: (domain: string) => request<RouteDetail>(`/routes/${encodeURIComponent(domain)}`),
    add: (body: { domain: string; forceHttps?: boolean; timeoutSeconds?: number; firstUpstreamUrl?: string }) =>
      request<CommandResult>('/routes', { method: 'POST', body: JSON.stringify(body) }),
    update: (domain: string, body: { forceHttps: boolean; timeoutSeconds: number }) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}`, { method: 'PUT', body: JSON.stringify(body) }),
    delete: (domain: string) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}`, { method: 'DELETE' }),
    addUpstream: (domain: string, body: { url: string; weight?: number }) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}/upstreams`, { method: 'POST', body: JSON.stringify(body) }),
    removeUpstream: (domain: string, url: string) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}/upstreams/${toBase64Url(url)}`, { method: 'DELETE' }),
  },
  certificates: {
    list: () => request<CertificateSummary[]>('/certificates'),
    get: (domain: string) => request<CertificateDetail>(`/certificates/${encodeURIComponent(domain)}`),
    provision: (domain: string) =>
      request<CommandResult>(`/certificates/${encodeURIComponent(domain)}/provision`, { method: 'POST' }),
    upload: (domain: string, file: File, keyFile?: File, password?: string) => {
      const form = new FormData()
      form.append('file', file)
      if (keyFile) { form.append('keyFile', keyFile) }
      if (password) { form.append('password', password) }
      return fetch(`${BASE}/certificates/${encodeURIComponent(domain)}/upload`, {
        method: 'POST',
        body: form,
      }).then(r => r.json() as Promise<CommandResult>)
    },
  },
  health: {
    get: () => request<ProxyHealthResponse>('/health'),
    getDomain: (domain: string) => request<UpstreamHealthResponse>(`/health/${encodeURIComponent(domain)}`),
  },
  discovery: {
    listContainers: () => request<DiscoveredContainer[]>('/discovery/containers'),
  },
  settings: {
    get: () => request<ProxySettings>('/settings'),
    update: (body: Partial<ProxySettings>) =>
      request<CommandResult>('/settings', { method: 'PUT', body: JSON.stringify(body) }),
  },
}
