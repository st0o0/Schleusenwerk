import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import type { ProxyEventDto } from './client'

type EventHandler = (event: ProxyEventDto) => void

let connection: HubConnection | null = null
const handlers: EventHandler[] = []

export function onProxyEvent(handler: EventHandler) {
  handlers.push(handler)
  return () => {
    const idx = handlers.indexOf(handler)
    if (idx >= 0) { handlers.splice(idx, 1) }
  }
}

export async function startSignalR() {
  if (connection) { return }

  const apiUrl = import.meta.env.VITE_API_URL ?? ''

  connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl}/hubs/events`)
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('OnProxyEvent', (evt: ProxyEventDto) => {
    handlers.forEach(h => h(evt))
  })

  await connection.start()
}

export function stopSignalR() {
  connection?.stop()
  connection = null
}
