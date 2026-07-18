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

export type SourceStatus = 'Connected' | 'Disconnected' | 'Error'

export const SOURCE_STATUSES: readonly SourceStatus[] = ['Connected', 'Disconnected', 'Error']

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
  /** Links back to the `SourceDefinition` that created this entry, when applicable. */
  id: string | null
  type: SourceType | null
}

// --- §4.2 Source management (OBS-style free source add/remove) --------------

export type SourceType = 'NDI' | 'WEBCAM' | 'SRT'

export const SOURCE_TYPES: readonly SourceType[] = ['NDI', 'WEBCAM', 'SRT']

export function isSourceType(value: unknown): value is SourceType {
  return typeof value === 'string' && (SOURCE_TYPES as readonly string[]).includes(value)
}

export interface NdiSourceConfig {
  source_name: string
}

export interface WebcamSourceConfig {
  device_id: string
  format: string | null
}

export interface SrtSourceConfig {
  url: string
  latency_ms: number
}

export type SourceDefinition =
  | {
      id: string
      name: string
      type: 'NDI'
      ndi: NdiSourceConfig
      webcam: null
      srt: null
    }
  | {
      id: string
      name: string
      type: 'WEBCAM'
      ndi: null
      webcam: WebcamSourceConfig
      srt: null
    }
  | {
      id: string
      name: string
      type: 'SRT'
      ndi: null
      webcam: null
      srt: SrtSourceConfig
    }

export function isSourceDefinition(value: unknown): value is SourceDefinition {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  if (typeof candidate.id !== 'string' || typeof candidate.name !== 'string') return false
  if (!isSourceType(candidate.type)) return false
  switch (candidate.type) {
    case 'NDI':
      return (
        typeof candidate.ndi === 'object' &&
        candidate.ndi !== null &&
        typeof (candidate.ndi as Record<string, unknown>).source_name === 'string'
      )
    case 'WEBCAM':
      return (
        typeof candidate.webcam === 'object' &&
        candidate.webcam !== null &&
        typeof (candidate.webcam as Record<string, unknown>).device_id === 'string'
      )
    case 'SRT':
      return (
        typeof candidate.srt === 'object' &&
        candidate.srt !== null &&
        typeof (candidate.srt as Record<string, unknown>).url === 'string'
      )
  }
}

// --- §4.2 Program bus (2-system ME) composition ------------------------------

export type PgmBus = 'PGM1' | 'PGM2'

export const PGM_BUSES: readonly PgmBus[] = ['PGM1', 'PGM2']

export function isPgmBus(value: unknown): value is PgmBus {
  return typeof value === 'string' && (PGM_BUSES as readonly string[]).includes(value)
}

export interface ProgramLayer {
  source_id: string
  pip: PipSettings
}

export interface ProgramRequest {
  bus: PgmBus
  /** Composited back-to-front. */
  layers: ProgramLayer[]
  /** When true, switches this bus's PVW to PGM (TAKE). */
  take: boolean
}

// --- §4.2 4x4 configurable multiview -----------------------------------------

export const MULTIVIEW_CELL_COUNT = 16

export type MultiviewCell = 'PGM1' | 'PGM2' | 'PVW1' | 'PVW2' | `SRC:${string}` | 'EMPTY'

export function isMultiviewCell(value: unknown): value is MultiviewCell {
  if (typeof value !== 'string') return false
  if (value === 'PGM1' || value === 'PGM2' || value === 'PVW1' || value === 'PVW2' || value === 'EMPTY') {
    return true
  }
  return value.startsWith('SRC:') && value.length > 'SRC:'.length
}

export interface MultiviewConfig {
  cells: MultiviewCell[]
}

export function isMultiviewConfig(value: unknown): value is MultiviewConfig {
  if (typeof value !== 'object' || value === null) return false
  const cells = (value as Record<string, unknown>).cells
  return Array.isArray(cells) && cells.length === MULTIVIEW_CELL_COUNT && cells.every(isMultiviewCell)
}

// --- §4.2 Output assignment (virtual cameras x2 + HDMI) ----------------------

export type OutputSink = 'VCAM1' | 'VCAM2' | 'HDMI'

export const OUTPUT_SINKS: readonly OutputSink[] = ['VCAM1', 'VCAM2', 'HDMI']

export function isOutputSink(value: unknown): value is OutputSink {
  return typeof value === 'string' && (OUTPUT_SINKS as readonly string[]).includes(value)
}

export type OutputAssignment =
  | { sink: 'VCAM1' | 'VCAM2'; source: PgmBus }
  | { sink: 'HDMI'; source: PgmBus; display_id: number; hide_cursor: boolean; fullscreen: boolean }

export interface OutputsConfig {
  outputs: OutputAssignment[]
}

// --- §4.2 Module assignment (physical src1/src2 -> logical source + VR target) ---

/** Upper bound on module count (parent spec §4.0 `MAX_MODULES`, HID report/address-space limit). */
export const MAX_MODULES = 8

export interface ModuleSrc {
  source_id: string | null
  /** e.g. "transition" | "opacity"; kept open-ended so new target kinds don't require a contract change. */
  vr_target: string
}

export interface ModuleMapping {
  index: number
  src1: ModuleSrc
  src2: ModuleSrc
}

export interface ModulesConfig {
  modules: ModuleMapping[]
}

// --- §4.6 Pico network configuration (Wi-Fi/BT credentials, PC-mediated) ----

export interface PicoNetworkConfig {
  wifi_ssid: string | null
  wifi_password: string | null
  controller_id: string | null
  bluetooth_enabled: boolean
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
// 2-bus payload (§4.3); single-bus deployments populate active_pgm1/active_pvw1 only
// and leave active_pgm2/active_pvw2 empty.

export interface TallyState {
  active_pgm1: number[]
  active_pgm2: number[]
  active_pvw1: number[]
  active_pvw2: number[]
}

export function isTallyState(value: unknown): value is TallyState {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  return (
    isNumberArray(candidate.active_pgm1) &&
    isNumberArray(candidate.active_pgm2) &&
    isNumberArray(candidate.active_pvw1) &&
    isNumberArray(candidate.active_pvw2)
  )
}

function isNumberArray(value: unknown): value is number[] {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'number')
}
