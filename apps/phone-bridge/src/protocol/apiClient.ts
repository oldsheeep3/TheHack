import type {
  AtemCommandRequest,
  AtemConfig,
  AtemDeviceInfo,
  AtemStreamingRequest,
  AudioDeviceInfo,
  AudioOutputsConfig,
  ConfigChangeRequest,
  DeviceInfo,
  DeviceQueryType,
  ModulesConfig,
  MultiviewLayout,
  OutputsConfig,
  PicoNetworkConfig,
  ProgramRequest,
  SourceDefinition,
  SourceInfo,
  SrtSetupInfo,
} from './types'

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

  // --- sources -------------------------------------------------------------

  async getSources(signal?: AbortSignal): Promise<SourceInfo[]> {
    return this.request<SourceInfo[]>('GET', '/api/v1/sources', undefined, signal)
  }

  /**
   * The per-type configuration behind each source. `getSources` reports the engine's runtime view
   * (channel/protocol/status) and carries none of it, so an edit form is populated from here — a `PUT`
   * replaces the whole definition, and a client that guessed would wipe the half it never read.
   */
  async getSourceDefinitions(signal?: AbortSignal): Promise<SourceDefinition[]> {
    return this.request<SourceDefinition[]>('GET', '/api/v1/sources/definitions', undefined, signal)
  }

  async addSource(source: SourceDefinition, signal?: AbortSignal): Promise<void> {
    await this.request<void>('POST', '/api/v1/sources', source, signal)
  }

  async updateSource(id: string, source: SourceDefinition, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', `/api/v1/sources/${encodeURIComponent(id)}`, source, signal)
  }

  async deleteSource(id: string, signal?: AbortSignal): Promise<void> {
    await this.request<void>('DELETE', `/api/v1/sources/${encodeURIComponent(id)}`, undefined, signal)
  }

  // --- device discovery ----------------------------------------------------

  /** Selectable capture devices of one kind. SRT is push-based, so it is not enumerable — see `getSrtSetup`. */
  async getDevices(type: DeviceQueryType, signal?: AbortSignal): Promise<DeviceInfo[]> {
    return this.request<DeviceInfo[]>('GET', `/api/v1/devices/${type}`, undefined, signal)
  }

  /** Listener port, LAN addresses and a ready-to-copy URL for pointing an SRT sender at the PC. */
  async getSrtSetup(signal?: AbortSignal): Promise<SrtSetupInfo> {
    return this.request<SrtSetupInfo>('GET', '/api/v1/srt/setup', undefined, signal)
  }

  // --- composition ---------------------------------------------------------

  async updateConfig(request: ConfigChangeRequest, signal?: AbortSignal): Promise<void> {
    await this.request<void>('POST', '/api/v1/config', request, signal)
  }

  async applyProgram(request: ProgramRequest, signal?: AbortSignal): Promise<void> {
    await this.request<void>('POST', '/api/v1/program', request, signal)
  }

  async getMultiview(signal?: AbortSignal): Promise<MultiviewLayout> {
    return this.request<MultiviewLayout>('GET', '/api/v1/multiview', undefined, signal)
  }

  async setMultiview(layout: MultiviewLayout, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/multiview', layout, signal)
  }

  // --- outputs -------------------------------------------------------------

  async getOutputs(signal?: AbortSignal): Promise<OutputsConfig> {
    return this.request<OutputsConfig>('GET', '/api/v1/outputs', undefined, signal)
  }

  async setOutputs(config: OutputsConfig, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/outputs', config, signal)
  }

  async getAudioDevices(signal?: AbortSignal): Promise<AudioDeviceInfo[]> {
    return this.request<AudioDeviceInfo[]>('GET', '/api/v1/audio/devices', undefined, signal)
  }

  async getAudioOutputs(signal?: AbortSignal): Promise<AudioOutputsConfig> {
    return this.request<AudioOutputsConfig>('GET', '/api/v1/audio/outputs', undefined, signal)
  }

  async setAudioOutputs(config: AudioOutputsConfig, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/audio/outputs', config, signal)
  }

  // --- modules / ATEM / Pico ----------------------------------------------

  async getModules(signal?: AbortSignal): Promise<ModulesConfig> {
    return this.request<ModulesConfig>('GET', '/api/v1/modules', undefined, signal)
  }

  async setModules(config: ModulesConfig, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/modules', config, signal)
  }

  async getAtemConfig(signal?: AbortSignal): Promise<AtemConfig> {
    return this.request<AtemConfig>('GET', '/api/v1/atem', undefined, signal)
  }

  async setAtemConfig(config: AtemConfig, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/atem', config, signal)
  }

  /** Sweeps the LAN for ATEM switchers. Slow by nature — the PC probes a whole subnet. */
  async discoverAtemDevices(signal?: AbortSignal): Promise<AtemDeviceInfo[]> {
    return this.request<AtemDeviceInfo[]>('GET', '/api/v1/atem/discover', undefined, signal)
  }

  /**
   * Points the connected ATEM's streaming output at a URL. Resolves false when the PC has no ATEM
   * connected (HTTP 409) — not a client error, so it is reported as a state rather than thrown.
   */
  async configureAtemStreaming(request: AtemStreamingRequest, signal?: AbortSignal): Promise<boolean> {
    try {
      await this.request<void>('POST', '/api/v1/atem/streaming', request, signal)
      return true
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) return false
      throw error
    }
  }

  async sendAtemCommand(command: AtemCommandRequest, signal?: AbortSignal): Promise<void> {
    await this.request<void>('POST', '/api/v1/atem/command', command, signal)
  }

  async setPicoNetwork(config: PicoNetworkConfig, signal?: AbortSignal): Promise<void> {
    await this.request<void>('PUT', '/api/v1/pico/network', config, signal)
  }

  private async request<T>(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
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
      throw new ApiError(response.status, await describeFailure(method, path, response))
    }

    if (response.status === 204) {
      return undefined as T
    }

    return (await response.json()) as T
  }
}

/**
 * The PC answers a rejected payload with `{ "errors": [...] }` naming the offending field. Surfacing
 * "PUT /api/v1/outputs failed: 400" instead would hide the one thing the operator needs to fix.
 */
async function describeFailure(method: string, path: string, response: Response): Promise<string> {
  const fallback = `${method} ${path} failed: ${response.status}`
  try {
    const body: unknown = await response.json()
    const errors = (body as { errors?: unknown })?.errors
    if (Array.isArray(errors) && errors.length > 0) {
      return `${fallback} — ${errors.map(String).join(' / ')}`
    }
  } catch {
    // Not a JSON problem document; the status line is all there is to report.
  }
  return fallback
}

/**
 * With nothing specified, the PC is wherever this page came from: it serves the settings UI from the
 * same Kestrel host as `/api/v1/*`. Taking the origin verbatim also keeps a non-default `WebPort`
 * working, which hard-coding 8080 would not.
 */
function resolveBaseUrl(options: ApiClientOptions): string {
  if (options.baseUrl) {
    return options.baseUrl.replace(/\/+$/, '')
  }
  if (options.host === undefined && options.port === undefined) {
    return window.location.origin
  }
  const host = options.host ?? window.location.hostname
  const port = options.port ?? DEFAULT_API_PORT
  return `http://${host}:${port}`
}
