import { defineStore } from 'pinia'
import { ref } from 'vue'
import { onProxyEvent, startSignalR } from '@/api/signalr'
import { i18n } from '@/i18n'
import type { ProxyEventDto } from '@/api/client'

export interface FlowEntry {
  time: string; type: string; domain: string; isHealthy: boolean; upstream: string; message: string
}

const MAX_EVENTS = 200

export const useEventsStore = defineStore('events', () => {
  const entries = ref<FlowEntry[]>([])
  const paused = ref(false)
  const connected = ref(false)
  const knownDomains = ref<Set<string>>(new Set())

  function timeLocale(): string {
    return i18n.global.locale.value === 'de' ? 'de-DE' : 'en-GB'
  }

  function init() {
    onProxyEvent((evt: ProxyEventDto) => {
      if (paused.value) { return }
      entries.value.unshift({
        time: new Date().toLocaleTimeString(timeLocale(), { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        type: evt.type, domain: evt.domain, isHealthy: evt.isHealthy,
        upstream: evt.upstreamUrl, message: evt.message,
      })
      if (evt.domain) { knownDomains.value.add(evt.domain) }
      if (entries.value.length > MAX_EVENTS) { entries.value.length = MAX_EVENTS }
    })
    startSignalR()
      .then(() => { connected.value = true })
      .catch(() => { connected.value = false })
  }

  function togglePause() { paused.value = !paused.value }

  return { entries, paused, connected, knownDomains, init, togglePause }
})
