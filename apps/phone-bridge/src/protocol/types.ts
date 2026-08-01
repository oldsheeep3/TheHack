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

export type SourceProtocol = 'UVC' | 'NDI' | 'SRT' | 'IMAGE' | 'HTML' | 'MIX'

export const SOURCE_PROTOCOLS: readonly SourceProtocol[] = ['UVC', 'NDI', 'SRT', 'IMAGE', 'HTML', 'MIX']

export function isSourceProtocol(value: unknown): value is SourceProtocol {
  return typeof value === 'string' && (SOURCE_PROTOCOLS as readonly string[]).includes(value)
}

export type SourceStatus = 'Connected' | 'Disconnected' | 'Error'

export const SOURCE_STATUSES: readonly SourceStatus[] = ['Connected', 'Disconnected', 'Error']

export function isSourceStatus(value: unknown): value is SourceStatus {
  return typeof value === 'string' && (SOURCE_STATUSES as readonly string[]).includes(value)
}

/**
 * Result of `GET /api/v1/sources` (one entry per input channel) — the engine's *runtime* view.
 * It carries no per-type configuration, so an edit form is populated from
 * `GET /api/v1/sources/definitions` instead.
 */
export interface SourceInfo {
  channel: number
  name: string
  protocol: SourceProtocol
  resolution: string | null
  status: SourceStatus
  /** Links back to the `SourceDefinition` that created this entry, when applicable. */
  id: string | null
  /** Operator-facing ordering the PC assigns; null for legacy channel-based sources. */
  order: number | null
}

// --- §4.2 Source management (OBS-style free source add/remove) --------------

export type SourceType = 'NDI' | 'WEBCAM' | 'SRT' | 'IMAGE' | 'HTML' | 'MIX'

export const SOURCE_TYPES: readonly SourceType[] = ['NDI', 'WEBCAM', 'SRT', 'IMAGE', 'HTML', 'MIX']

export function isSourceType(value: unknown): value is SourceType {
  return typeof value === 'string' && (SOURCE_TYPES as readonly string[]).includes(value)
}

/** Operator-facing label per source type (the wire token stays the identifier). */
export const SOURCE_TYPE_LABEL: Record<SourceType, string> = {
  NDI: 'NDI',
  WEBCAM: 'ウェブカメラ',
  SRT: 'SRT',
  IMAGE: '静止画',
  HTML: 'Webページ',
  MIX: 'ミックス',
}

/**
 * How a source's audio reaches the program buses. A source carries its own audio — there is no
 * separate "audio source" to create — so this rides on the definition rather than on a mixer table.
 */
export type SourceAudioMode = 'OFF' | 'ON' | 'AFV'

export const SOURCE_AUDIO_MODES: readonly SourceAudioMode[] = ['OFF', 'ON', 'AFV']

export const SOURCE_AUDIO_MODE_LABEL: Record<SourceAudioMode, string> = {
  OFF: 'OFF（無音）',
  ON: 'ON（常時オン）',
  AFV: 'AFV（映像に追従）',
}

export function isSourceAudioMode(value: unknown): value is SourceAudioMode {
  return typeof value === 'string' && (SOURCE_AUDIO_MODES as readonly string[]).includes(value)
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

/** Still image loaded from a local file (png/jpg/gif/webp/bmp). */
export interface ImageSourceConfig {
  file_path: string
}

/**
 * Browser source. `url` is an `http(s)://` address, or a path to a local `.html` file when
 * `is_local_file` is set. `width`/`height` are the page's own layout resolution — it is rendered
 * off-screen at that size and scaled into its scene item, not cropped to it.
 */
export interface HtmlSourceConfig {
  url: string
  width: number
  height: number
  is_local_file: boolean
  fps: number
  css?: string | null
}

/** One member of a `MixSourceConfig`: another source placed at a rectangle on the mix canvas. */
export interface MixLayer {
  source_id: string
  x_position: number
  y_position: number
  width: number
  height: number
  /** Bottom-up; a higher value draws on top. */
  z_order: number
  crop?: CropRect | null
}

/**
 * Several sources composited into one, addressable anywhere a single source is (bus layer, multiview
 * cell, module binding). Members stay independently usable — the PC opens each device once and shares it.
 */
export interface MixSourceConfig {
  layers: MixLayer[]
  canvas_width: number
  canvas_height: number
}

interface SourceDefinitionBase {
  id: string
  name: string
  /** Defaults to `AFV` on the PC when omitted. */
  audio_mode?: SourceAudioMode
}

/**
 * Request body for the source CRUD endpoints. Exactly one per-type config member is set, matching
 * `type`; the PC reads a missing member as null, so the unused variants are simply left out.
 */
export type SourceDefinition = SourceDefinitionBase &
  (
    | { type: 'NDI'; ndi: NdiSourceConfig }
    | { type: 'WEBCAM'; webcam: WebcamSourceConfig }
    | { type: 'SRT'; srt: SrtSourceConfig }
    | { type: 'IMAGE'; image: ImageSourceConfig }
    | { type: 'HTML'; html: HtmlSourceConfig }
    | { type: 'MIX'; mix: MixSourceConfig }
  )

export function isSourceDefinition(value: unknown): value is SourceDefinition {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  if (typeof candidate.id !== 'string' || typeof candidate.name !== 'string') return false
  if (!isSourceType(candidate.type)) return false
  if (candidate.audio_mode !== undefined && !isSourceAudioMode(candidate.audio_mode)) return false

  switch (candidate.type) {
    case 'NDI':
      return hasStringMember(candidate.ndi, 'source_name')
    case 'WEBCAM':
      return hasStringMember(candidate.webcam, 'device_id')
    case 'SRT':
      return hasStringMember(candidate.srt, 'url')
    case 'IMAGE':
      return hasStringMember(candidate.image, 'file_path')
    case 'HTML':
      return hasStringMember(candidate.html, 'url')
    case 'MIX':
      return (
        typeof candidate.mix === 'object' &&
        candidate.mix !== null &&
        Array.isArray((candidate.mix as Record<string, unknown>).layers) &&
        (candidate.mix as { layers: unknown[] }).layers.length > 0
      )
  }
}

function hasStringMember(value: unknown, member: string): boolean {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as Record<string, unknown>)[member] === 'string' &&
    ((value as Record<string, string>)[member]).length > 0
  )
}

// --- §4.1 Device enumeration (GET /api/v1/devices/{type}, GET /api/v1/srt/setup) ---

/** Path segment of `GET /api/v1/devices/{type}`. SRT is push-based and therefore not enumerable. */
export type DeviceQueryType = 'webcam' | 'ndi'

/** A discovered input device. `formats` lists selectable "WxH@FPS" candidates for webcams. */
export interface DeviceInfo {
  id: string
  name: string
  formats: string[] | null
}

/**
 * Guidance for pointing an SRT sender at this PC (`GET /api/v1/srt/setup`). `host_candidates` are the
 * PC's LAN IPv4 addresses; `recommended_url` is ready to paste into the encoder.
 */
export interface SrtSetupInfo {
  listener_port: number
  host_candidates: string[]
  recommended_url: string
  recommended_latency_ms: number
  instructions_text: string
}

/**
 * Which end opens the connection. The PC stores this in the URL's `mode=` query (§4.3): FFmpeg's
 * libsrt defaults to `caller`, which is what makes a Listener setup look dead — the PC dials out
 * instead of binding the port.
 */
export type SrtMode = 'listener' | 'caller'

export const SRT_MODE_LABEL: Record<SrtMode, string> = {
  listener: 'Listener（PCが待ち受け／送信側が接続してくる）',
  caller: 'Caller（PCから接続しに行く）',
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
  /** When true, switches this bus's PVW to PGM (TAKE). This settings client never sets it. */
  take: boolean
}

// --- §4.2 Configurable multiview ---------------------------------------------
// Two wire forms. `cells` is the legacy fixed 4x4 (16 entries); `grid` + `regions` is the current one,
// which is what the PC's own multiview editor writes: an arbitrary grid whose regions may span cells.

export const MULTIVIEW_CELL_COUNT = 16

/** Grid bounds the PC accepts (mirrors `MultiviewLayoutValidator` / `MultiviewRegionModel`). */
export const MULTIVIEW_MIN_GRID = 4

export const MULTIVIEW_MAX_GRID = 6

export type MultiviewCell = 'PGM1' | 'PGM2' | 'PVW1' | 'PVW2' | `SRC:${string}` | 'EMPTY'

export function isMultiviewCell(value: unknown): value is MultiviewCell {
  if (typeof value !== 'string') return false
  if (value === 'PGM1' || value === 'PGM2' || value === 'PVW1' || value === 'PVW2' || value === 'EMPTY') {
    return true
  }
  return value.startsWith('SRC:') && value.length > 'SRC:'.length
}

export interface MultiviewGrid {
  rows: number
  cols: number
}

/** One rectangular region of the grid. A 1x1 region is a plain cell. */
export interface MultiviewRegion {
  row: number
  col: number
  row_span: number
  col_span: number
  content: MultiviewCell
}

/**
 * Body of `PUT /api/v1/multiview` and reply of the matching `GET`. The PC rejects a payload that
 * carries both representations or neither, so exactly one of `cells` / `regions` is populated.
 */
export interface MultiviewLayout {
  cells?: MultiviewCell[]
  grid?: MultiviewGrid | null
  regions?: MultiviewRegion[] | null
}

export function isMultiviewRegion(value: unknown): value is MultiviewRegion {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  return (
    typeof candidate.row === 'number' &&
    typeof candidate.col === 'number' &&
    typeof candidate.row_span === 'number' &&
    typeof candidate.col_span === 'number' &&
    isMultiviewCell(candidate.content)
  )
}

/** True for the legacy 16-cell form (the only one older PCs wrote). */
export function isLegacyMultiviewCells(value: unknown): value is { cells: MultiviewCell[] } {
  if (typeof value !== 'object' || value === null) return false
  const cells = (value as Record<string, unknown>).cells
  return Array.isArray(cells) && cells.length === MULTIVIEW_CELL_COUNT && cells.every(isMultiviewCell)
}

// --- §4.2 Output assignment (operator-built table: webcam / HDMI / NDI) ------
// The table is no longer a fixed set of sinks. It starts at one webcam plus one display and the
// operator adds sinks of a kind, at most maxOutputsOf(kind) of each and MAX_OUTPUTS in total.

export type OutputKind = 'WEBCAM' | 'HDMI' | 'NDI'

export const OUTPUT_KINDS: readonly OutputKind[] = ['WEBCAM', 'HDMI', 'NDI']

export function isOutputKind(value: unknown): value is OutputKind {
  return typeof value === 'string' && (OUTPUT_KINDS as readonly string[]).includes(value)
}

export const OUTPUT_KIND_LABEL: Record<OutputKind, string> = {
  WEBCAM: 'Webcam',
  HDMI: 'HDMI',
  NDI: 'NDI',
}

export type WebcamOutputSink = 'VCAM1' | 'VCAM2' | 'VCAM3'
export type HdmiOutputSink = 'HDMI1' | 'HDMI2' | 'HDMI3'
export type NdiOutputSink = 'NDI1' | 'NDI2' | 'NDI3'

export type OutputSink = WebcamOutputSink | HdmiOutputSink | NdiOutputSink

export const WEBCAM_OUTPUT_SINKS: readonly WebcamOutputSink[] = ['VCAM1', 'VCAM2', 'VCAM3']

export const HDMI_OUTPUT_SINKS: readonly HdmiOutputSink[] = ['HDMI1', 'HDMI2', 'HDMI3']

export const NDI_OUTPUT_SINKS: readonly NdiOutputSink[] = ['NDI1', 'NDI2', 'NDI3']

export const OUTPUT_SINKS: readonly OutputSink[] = [
  ...WEBCAM_OUTPUT_SINKS,
  ...HDMI_OUTPUT_SINKS,
  ...NDI_OUTPUT_SINKS,
]

/**
 * Most webcam sinks the output table may hold — one, because OBS exposes a single virtual camera. A
 * second webcam sink could only be honoured by sending it somewhere that is not a camera, so it is not
 * offered. `VCAM2`/`VCAM3` keep their tokens only so configs written by older builds still parse.
 */
export const MAX_WEBCAM_OUTPUTS = 1

/** Most sinks of any other kind the output table may hold. */
export const MAX_SINKS_PER_KIND = 3

/** Most sinks of `kind` the output table may hold. */
export function maxOutputsOf(kind: OutputKind): number {
  return kind === 'WEBCAM' ? MAX_WEBCAM_OUTPUTS : MAX_SINKS_PER_KIND
}

/** Most sinks the output table may hold in total, across all kinds. */
export const MAX_OUTPUTS = 6

/**
 * Only canonical ordinal tokens, and every token a table may carry rather than every sink an operator
 * may add — `VCAM2`/`VCAM3` still parse so tables written by older builds load. Builds that predate
 * multiple sinks per kind wrote `"HDMI"`/`"VCAM"` without an ordinal; the PC reads those as the first
 * sink of the kind and rewrites them canonically, so nothing this client is served still carries them.
 */
export function isOutputSink(value: unknown): value is OutputSink {
  return typeof value === 'string' && (OUTPUT_SINKS as readonly string[]).includes(value)
}

/** The kind `sink` belongs to. */
export function outputKindOf(sink: OutputSink): OutputKind {
  if ((WEBCAM_OUTPUT_SINKS as readonly string[]).includes(sink)) return 'WEBCAM'
  if ((HDMI_OUTPUT_SINKS as readonly string[]).includes(sink)) return 'HDMI'
  return 'NDI'
}

/** Every sink of `kind` that has a wire token, in ordinal order. */
export function outputSinksOf(kind: OutputKind): readonly OutputSink[] {
  if (kind === 'WEBCAM') return WEBCAM_OUTPUT_SINKS
  if (kind === 'HDMI') return HDMI_OUTPUT_SINKS
  return NDI_OUTPUT_SINKS
}

export interface WebcamOutputAssignment {
  sink: WebcamOutputSink
  source: PgmBus
}

export interface HdmiOutputAssignment {
  sink: HdmiOutputSink
  source: PgmBus
  display_id: number
  hide_cursor: boolean
  fullscreen: boolean
}

export interface NdiOutputAssignment {
  sink: NdiOutputSink
  source: PgmBus
  /** Sender name published on the network; the PC fills in a default when omitted. */
  ndi_name?: string | null
}

export type OutputAssignment = WebcamOutputAssignment | HdmiOutputAssignment | NdiOutputAssignment

export function isHdmiOutput(value: OutputAssignment): value is HdmiOutputAssignment {
  return (HDMI_OUTPUT_SINKS as readonly string[]).includes(value.sink)
}

export function isNdiOutput(value: OutputAssignment): value is NdiOutputAssignment {
  return (NDI_OUTPUT_SINKS as readonly string[]).includes(value.sink)
}

export interface OutputsConfig {
  outputs: OutputAssignment[]
}

/** Display an HDMI sink targets until the operator picks another: the primary monitor. */
export const DEFAULT_HDMI_DISPLAY_ID = 0

/** Sender name a new NDI sink gets when the operator does not type one (matches `OutputDefaults`). */
export function defaultNdiSenderName(sink: NdiOutputSink): string {
  return sink === 'NDI2' ? 'SWITCHER PGM2' : sink === 'NDI3' ? 'SWITCHER PGM3' : 'SWITCHER PGM1'
}

// --- §4.2 Audio output routing (GET/PUT /api/v1/audio/outputs) ---------------

/** An audio render endpoint the PC can play to. */
export interface AudioDeviceInfo {
  id: string
  name: string
  is_default: boolean
}

/**
 * Routes one program bus to one audio output device. A bus may appear more than once to feed several
 * devices at the same time, and each bus is independent. An empty `device_id` means the system default.
 */
export interface AudioOutputAssignment {
  bus: PgmBus
  device_id: string
  device_name?: string | null
}

export interface AudioOutputsConfig {
  outputs: AudioOutputAssignment[]
}

// --- §4.2 ATEM remote control (GET/PUT /api/v1/atem, /discover, /streaming, /command) ---

/** ATEM operations reachable from a mapped module switch. */
export type AtemAction = 'ProgramInput' | 'PreviewInput' | 'Cut' | 'Auto'

export const ATEM_ACTIONS: readonly AtemAction[] = ['ProgramInput', 'PreviewInput', 'Cut', 'Auto']

/** Cut/Auto act on the whole M/E, so an input number is only meaningful for the two input actions. */
export function atemActionTakesSource(action: AtemAction): boolean {
  return action === 'ProgramInput' || action === 'PreviewInput'
}

/** One of a module's 4 physical switches, as the PC names them on the wire. */
export type ModuleSwitchId = 'Pgm1Src1' | 'Pgm1Src2' | 'Pgm2Src1' | 'Pgm2Src2'

export const MODULE_SWITCH_IDS: readonly ModuleSwitchId[] = ['Pgm1Src1', 'Pgm1Src2', 'Pgm2Src1', 'Pgm2Src2']

export const MODULE_SWITCH_LABEL: Record<ModuleSwitchId, string> = {
  Pgm1Src1: 'PGM1 · SRC1',
  Pgm1Src2: 'PGM1 · SRC2',
  Pgm2Src1: 'PGM2 · SRC1',
  Pgm2Src2: 'PGM2 · SRC2',
}

/**
 * `controller_id` the PC uses for mappings relayed from the HID module panel — the same value its own
 * ATEM settings window writes, so a mapping made here and one made there are the same row.
 */
export const HID_RELAY_CONTROLLER_ID = 'hid'

export interface AtemButtonMapping {
  controller_id: string
  module_index: number
  switch: ModuleSwitchId | string
  action: AtemAction | string
  /** 0-based M/E index. */
  mix_effect: number
  /** ATEM input number (1-8 on a Mini; 0 is black). */
  source: number
}

export interface AtemConfig {
  enabled: boolean
  ip: string
  mappings: AtemButtonMapping[]
  /** Model name reported during discovery. Display only; `ip` is what the PC connects to. */
  name?: string | null
}

/** One switcher found by a network sweep (`GET /api/v1/atem/discover`). */
export interface AtemDeviceInfo {
  ip: string
  name: string
}

/**
 * Points the connected ATEM's streaming output at a URL — an `srt://` one, to feed its ON AIR program
 * into this PC as a source — and optionally puts it on air.
 */
export interface AtemStreamingRequest {
  url: string
  service_name?: string
  /** Empty for SRT, which does not use one. */
  stream_key?: string
  start?: boolean
}

/** A one-off Program/Preview switch or Cut/Auto (`POST /api/v1/atem/command`). */
export interface AtemCommandRequest {
  action: AtemAction
  mix_effect: number
  source: number
}

// --- §4.2 Module assignment (physical src1/src2 -> logical source + VR target) ---

/** Upper bound on module count (parent spec §4.0 `MAX_MODULES`, HID report/address-space limit). */
export const MAX_MODULES = 8

/** VR (potentiometer) targets the PC knows about. Open-ended on the wire, so free text is accepted. */
export const VR_TARGETS: readonly string[] = ['transition', 'opacity', 'assignable']

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

// --- §4.3 Tally state (UDP broadcast) ---------------------------------------
// Kept as the wire mirror of the PC's broadcast payload. There is no REST endpoint serving it: the PC
// publishes tally as a UDP broadcast to 255.255.255.255:9999, which a browser cannot receive — and this
// client is settings-only, so it does not need it.

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
