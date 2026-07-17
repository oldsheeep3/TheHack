/**
 * Shared protocol types, mirroring the PC-side `Switcher.Contracts` schema
 * (see docs/specs/00-system-overview.md §4). JSON wire fields are snake_case;
 * TS-side property names match the JSON keys verbatim so the client can
 * (de)serialize without a mapping layer.
 */

// --- §4.1 Controller input event (WebSocket) ---------------------------------

export interface ButtonEvent {
  controller_id: string
  button_id: number
  timestamp: number
}

export type WsEnvelope = ButtonPressEnvelope

export interface ButtonPressEnvelope {
  event: 'button_press'
  data: ButtonEvent
}

export function isWsEnvelope(value: unknown): value is WsEnvelope {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  if (candidate.event !== 'button_press') return false
  const data = candidate.data
  if (typeof data !== 'object' || data === null) return false
  const event = data as Record<string, unknown>
  return (
    typeof event.controller_id === 'string' &&
    typeof event.button_id === 'number' &&
    typeof event.timestamp === 'number'
  )
}

// --- §4.2 Switcher config change API (POST /api/v1/config) ------------------

export type SourceProtocol = 'UVC' | 'NDI' | 'SRT'

export const SOURCE_PROTOCOLS: readonly SourceProtocol[] = ['UVC', 'NDI', 'SRT']

export function isSourceProtocol(value: unknown): value is SourceProtocol {
  return typeof value === 'string' && (SOURCE_PROTOCOLS as readonly string[]).includes(value)
}

export type SourceStatus = 'connected' | 'disconnected' | 'error'

export const SOURCE_STATUSES: readonly SourceStatus[] = ['connected', 'disconnected', 'error']

export function isSourceStatus(value: unknown): value is SourceStatus {
  return typeof value === 'string' && (SOURCE_STATUSES as readonly string[]).includes(value)
}

/** Result of `GET /api/v1/sources` (one entry per input channel). */
export interface SourceInfo {
  channel: number
  name: string
  protocol: SourceProtocol
  resolution: string | null
  status: SourceStatus
}

export interface CropRect {
  left: number
  top: number
  right: number
  bottom: number
}

export interface PipSettings {
  enabled: boolean
  x_position: number
  y_position: number
  width: number
  height: number
  opacity: number
  /** Stacking order among active PiP layers; optional, higher draws on top. */
  z_order?: number
  crop?: CropRect | null
}

export interface ConfigChangeRequest {
  target_channel: number
  source_type: SourceProtocol
  source_url: string | null
  pip_settings: PipSettings | null
}

// --- §4.3 Tally state (UDP broadcast, mirrored to the web client over REST/WS) ---

export interface TallyState {
  active_pgm: number[]
  active_pvw: number[]
}
