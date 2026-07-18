import { ApiClient } from '../protocol/apiClient'
import { WsClient, type WsConnectionState } from '../protocol/wsClient'
import { DEFAULT_PORT, loadHostConfig, saveHostConfig } from './hostConfig'

export type NetworkReachability = 'unknown' | 'checking' | 'reachable' | 'unreachable'

export interface ConnectionManagerOptions {
  reachabilityPollMs?: number
}

type Unsubscribe = () => void

const DEFAULT_REACHABILITY_POLL_MS = 5000

/**
 * Owns the ApiClient/WsClient pair pointed at the PC host (§2.1). Reachability
 * is inferred from `GET /api/v1/sources` polling since there's no dedicated
 * health endpoint. Changing the connection target reconfigures the existing
 * ApiClient in place and recreates the WsClient (its target URL is immutable
 * once constructed).
 */
export class ConnectionManager {
  private host: string
  private port: number
  private readonly apiClient: ApiClient
  private wsClient: WsClient
  private unsubscribeWs: Unsubscribe | null = null
  private reachability: NetworkReachability = 'unknown'
  private wsState: WsConnectionState = 'idle'
  private reachabilityTimer: ReturnType<typeof setInterval> | null = null
  private reachabilityInFlight: AbortController | null = null
  private started = false
  private readonly reachabilityListeners = new Set<(state: NetworkReachability) => void>()
  private readonly wsStateListeners = new Set<(state: WsConnectionState) => void>()
  private readonly reachabilityPollMs: number

  constructor(options: ConnectionManagerOptions = {}) {
    const stored = loadHostConfig()
    this.host = stored?.host ?? window.location.hostname
    this.port = stored?.port ?? DEFAULT_PORT
    this.reachabilityPollMs = options.reachabilityPollMs ?? DEFAULT_REACHABILITY_POLL_MS
    this.apiClient = new ApiClient({ host: this.host, port: this.port })
    this.wsClient = new WsClient({ host: this.host, port: this.port })
  }

  getHost(): string {
    return this.host
  }

  getPort(): number {
    return this.port
  }

  getApiClient(): ApiClient {
    return this.apiClient
  }

  getReachability(): NetworkReachability {
    return this.reachability
  }

  getWsState(): WsConnectionState {
    return this.wsState
  }

  onReachabilityChange(listener: (state: NetworkReachability) => void): Unsubscribe {
    this.reachabilityListeners.add(listener)
    return () => this.reachabilityListeners.delete(listener)
  }

  onWsStateChange(listener: (state: WsConnectionState) => void): Unsubscribe {
    this.wsStateListeners.add(listener)
    return () => this.wsStateListeners.delete(listener)
  }

  /** Starts the WS connection and REST reachability polling. Idempotent. */
  start(): void {
    if (this.started) return
    this.started = true
    this.attachWsClient()
    this.wsClient.connect()
    this.startReachabilityPoll()
  }

  /** Stops the WS connection and polling; safe to call from an unmount cleanup. */
  stop(): void {
    this.started = false
    this.unsubscribeWs?.()
    this.unsubscribeWs = null
    this.wsClient.disconnect()
    this.stopReachabilityPoll()
  }

  /** Reconfigures the connection target, persists it, and reconnects if already started. */
  setHost(host: string, port: number): void {
    this.host = host
    this.port = port
    saveHostConfig({ host, port })
    this.apiClient.setBaseUrl(`http://${host}:${port}`)

    const wasStarted = this.started
    if (wasStarted) this.stop()
    this.wsClient = new WsClient({ host, port })
    if (wasStarted) this.start()
  }

  private attachWsClient(): void {
    this.unsubscribeWs = this.wsClient.onStateChange((state) => {
      this.wsState = state
      for (const listener of this.wsStateListeners) listener(state)
    })
  }

  private startReachabilityPoll(): void {
    const poll = (): void => {
      this.reachabilityInFlight?.abort()
      const controller = new AbortController()
      this.reachabilityInFlight = controller
      this.setReachability('checking')
      this.apiClient
        .getSources(controller.signal)
        .then(() => this.setReachability('reachable'))
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === 'AbortError') return
          this.setReachability('unreachable')
        })
    }
    poll()
    this.reachabilityTimer = setInterval(poll, this.reachabilityPollMs)
  }

  private stopReachabilityPoll(): void {
    if (this.reachabilityTimer !== null) {
      clearInterval(this.reachabilityTimer)
      this.reachabilityTimer = null
    }
    this.reachabilityInFlight?.abort()
    this.reachabilityInFlight = null
    this.setReachability('unknown')
  }

  private setReachability(state: NetworkReachability): void {
    this.reachability = state
    for (const listener of this.reachabilityListeners) listener(state)
  }
}
