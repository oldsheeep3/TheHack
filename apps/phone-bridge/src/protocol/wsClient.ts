import { isWsEnvelope, type WsEnvelope } from './types'

export const DEFAULT_WS_PORT = 8080

export type WsConnectionState = 'idle' | 'connecting' | 'open' | 'reconnecting' | 'closed'

export interface WsClientOptions {
  /** e.g. "192.168.1.50". Defaults to the current host. */
  host?: string
  port?: number
  /** Overrides host/port entirely, e.g. "ws://192.168.1.50:8080/ws". */
  url?: string
  /** Base delay for reconnect backoff, in ms. Defaults to 500ms, doubling up to 10s. */
  reconnectBaseDelayMs?: number
  reconnectMaxDelayMs?: number
}

type Unsubscribe = () => void

/**
 * WebSocket client for the controller-input relay (§4.1). Reconnects
 * automatically with exponential backoff and exposes a small subscribable
 * connection-state store so the UI can render a status indicator.
 */
export class WsClient {
  private socket: WebSocket | null = null
  private state: WsConnectionState = 'idle'
  private readonly stateListeners = new Set<(state: WsConnectionState) => void>()
  private readonly messageListeners = new Set<(envelope: WsEnvelope) => void>()
  private reconnectAttempt = 0
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private closedByUser = false

  private readonly url: string
  private readonly reconnectBaseDelayMs: number
  private readonly reconnectMaxDelayMs: number

  constructor(options: WsClientOptions = {}) {
    this.url = resolveWsUrl(options)
    this.reconnectBaseDelayMs = options.reconnectBaseDelayMs ?? 500
    this.reconnectMaxDelayMs = options.reconnectMaxDelayMs ?? 10_000
  }

  getState(): WsConnectionState {
    return this.state
  }

  onStateChange(listener: (state: WsConnectionState) => void): Unsubscribe {
    this.stateListeners.add(listener)
    return () => this.stateListeners.delete(listener)
  }

  onMessage(listener: (envelope: WsEnvelope) => void): Unsubscribe {
    this.messageListeners.add(listener)
    return () => this.messageListeners.delete(listener)
  }

  connect(): void {
    this.closedByUser = false
    this.openSocket()
  }

  disconnect(): void {
    this.closedByUser = true
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.socket?.close()
    this.socket = null
    this.setState('closed')
  }

  send(envelope: WsEnvelope): void {
    if (this.socket?.readyState !== WebSocket.OPEN) {
      throw new Error('WsClient: cannot send while the connection is not open')
    }
    this.socket.send(JSON.stringify(envelope))
  }

  private openSocket(): void {
    this.setState(this.reconnectAttempt > 0 ? 'reconnecting' : 'connecting')

    const socket = new WebSocket(this.url)
    this.socket = socket

    socket.onopen = () => {
      this.reconnectAttempt = 0
      this.setState('open')
    }

    socket.onmessage = (event) => {
      const parsed = safeParseJson(event.data)
      if (isWsEnvelope(parsed)) {
        for (const listener of this.messageListeners) listener(parsed)
      }
    }

    socket.onclose = () => {
      this.socket = null
      if (this.closedByUser) {
        this.setState('closed')
        return
      }
      this.scheduleReconnect()
    }

    socket.onerror = () => {
      socket.close()
    }
  }

  private scheduleReconnect(): void {
    this.setState('reconnecting')
    const delay = Math.min(
      this.reconnectBaseDelayMs * 2 ** this.reconnectAttempt,
      this.reconnectMaxDelayMs,
    )
    this.reconnectAttempt += 1
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null
      this.openSocket()
    }, delay)
  }

  private setState(state: WsConnectionState): void {
    this.state = state
    for (const listener of this.stateListeners) listener(state)
  }
}

function resolveWsUrl(options: WsClientOptions): string {
  if (options.url) return options.url
  const host = options.host ?? window.location.hostname
  const port = options.port ?? DEFAULT_WS_PORT
  return `ws://${host}:${port}/ws`
}

function safeParseJson(data: unknown): unknown {
  if (typeof data !== 'string') return undefined
  try {
    return JSON.parse(data)
  } catch {
    return undefined
  }
}
