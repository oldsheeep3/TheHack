import type { ConfigChangeRequest, SourceInfo, TallyState } from './types'

export const DEFAULT_API_PORT = 8080

export interface ApiClientOptions {
  /** e.g. "192.168.1.50" or "192.168.1.50:8080". Defaults to the current host. */
  host?: string
  port?: number
  /** Overrides host/port entirely, e.g. "http://192.168.1.50:8080". */
  baseUrl?: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

export class ApiClient {
  private baseUrl: string

  constructor(options: ApiClientOptions = {}) {
    this.baseUrl = resolveBaseUrl(options)
  }

  setBaseUrl(baseUrl: string): void {
    this.baseUrl = baseUrl.replace(/\/+$/, '')
  }

  getBaseUrl(): string {
    return this.baseUrl
  }

  async getSources(signal?: AbortSignal): Promise<SourceInfo[]> {
    return this.request<SourceInfo[]>('GET', '/api/v1/sources', undefined, signal)
  }

  async updateConfig(request: ConfigChangeRequest, signal?: AbortSignal): Promise<void> {
    await this.request<void>('POST', '/api/v1/config', request, signal)
  }

  /**
   * Polls the last-known tally state (§4.3). The PC's primary tally path is a
   * UDP broadcast browsers cannot receive, so this assumes a small REST mirror
   * at `GET /api/v1/tally`; see phone-bridge README for the integration note.
   */
  async getTallyState(signal?: AbortSignal): Promise<TallyState> {
    return this.request<TallyState>('GET', '/api/v1/tally', undefined, signal)
  }

  private async request<T>(
    method: 'GET' | 'POST',
    path: string,
    body?: unknown,
    signal?: AbortSignal,
  ): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      method,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal,
    })

    if (!response.ok) {
      throw new ApiError(response.status, `${method} ${path} failed: ${response.status}`)
    }

    if (response.status === 204) {
      return undefined as T
    }

    return (await response.json()) as T
  }
}

function resolveBaseUrl(options: ApiClientOptions): string {
  if (options.baseUrl) {
    return options.baseUrl.replace(/\/+$/, '')
  }
  const host = options.host ?? window.location.hostname
  const port = options.port ?? DEFAULT_API_PORT
  return `http://${host}:${port}`
}
