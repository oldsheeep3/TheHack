/**
 * Pure conversion between a flat, form-friendly value bag and the
 * `SourceDefinition` discriminated union (§4.2). Kept free of React so the
 * type-out logic and validation are unit testable without a rendered form.
 */
import type { MixLayer, SourceAudioMode, SourceDefinition, SourceType, SrtMode } from '../protocol/types'
import { srtModeOf, srtUrlForMode } from './srtUrl'

/** Canvas a mix composites on until the operator changes it (matches `EngineDefaults`). */
export const DEFAULT_CANVAS_WIDTH = 1920

export const DEFAULT_CANVAS_HEIGHT = 1080

export const DEFAULT_SRT_LATENCY_MS = 40

export interface SourceFormValues {
  id: string
  name: string
  type: SourceType
  audioMode: SourceAudioMode
  ndiSourceName: string
  webcamDeviceId: string
  webcamFormat: string
  srtUrl: string
  srtMode: SrtMode
  srtLatencyMs: number
  imageFilePath: string
  htmlUrl: string
  htmlWidth: number
  htmlHeight: number
  htmlFps: number
  htmlIsLocalFile: boolean
  htmlCss: string
  mixLayers: MixLayer[]
  mixCanvasWidth: number
  mixCanvasHeight: number
}

export function emptySourceFormValues(type: SourceType = 'NDI'): SourceFormValues {
  return {
    id: '',
    name: '',
    type,
    audioMode: 'AFV',
    ndiSourceName: '',
    webcamDeviceId: '',
    webcamFormat: '',
    srtUrl: '',
    srtMode: 'listener',
    srtLatencyMs: DEFAULT_SRT_LATENCY_MS,
    imageFilePath: '',
    htmlUrl: '',
    htmlWidth: DEFAULT_CANVAS_WIDTH,
    htmlHeight: DEFAULT_CANVAS_HEIGHT,
    htmlFps: 30,
    htmlIsLocalFile: false,
    htmlCss: '',
    mixLayers: [],
    mixCanvasWidth: DEFAULT_CANVAS_WIDTH,
    mixCanvasHeight: DEFAULT_CANVAS_HEIGHT,
  }
}

/** A mix layer covering the whole canvas, which is what a first member should be. */
export function fullFrameMixLayer(
  sourceId: string,
  canvasWidth: number,
  canvasHeight: number,
  zOrder: number,
): MixLayer {
  return {
    source_id: sourceId,
    x_position: 0,
    y_position: 0,
    width: canvasWidth,
    height: canvasHeight,
    z_order: zOrder,
    crop: null,
  }
}

export function sourceFormValuesFromDefinition(source: SourceDefinition): SourceFormValues {
  const values = emptySourceFormValues(source.type)
  values.id = source.id
  values.name = source.name
  values.audioMode = source.audio_mode ?? 'AFV'

  switch (source.type) {
    case 'NDI':
      values.ndiSourceName = source.ndi.source_name
      break
    case 'WEBCAM':
      values.webcamDeviceId = source.webcam.device_id
      values.webcamFormat = source.webcam.format ?? ''
      break
    case 'SRT':
      values.srtUrl = source.srt.url
      values.srtMode = srtModeOf(source.srt.url)
      values.srtLatencyMs = source.srt.latency_ms
      break
    case 'IMAGE':
      values.imageFilePath = source.image.file_path
      break
    case 'HTML':
      values.htmlUrl = source.html.url
      values.htmlWidth = source.html.width
      values.htmlHeight = source.html.height
      values.htmlFps = source.html.fps
      values.htmlIsLocalFile = source.html.is_local_file
      values.htmlCss = source.html.css ?? ''
      break
    case 'MIX':
      values.mixLayers = source.mix.layers.map((layer) => ({ ...layer }))
      values.mixCanvasWidth = source.mix.canvas_width
      values.mixCanvasHeight = source.mix.canvas_height
      break
  }

  return values
}

/** Builds the `SourceDefinition` discriminated union to send to the source CRUD endpoints. */
export function buildSourceDefinition(values: SourceFormValues): SourceDefinition {
  const base = { id: values.id.trim(), name: values.name.trim(), audio_mode: values.audioMode }
  switch (values.type) {
    case 'NDI':
      return { ...base, type: 'NDI', ndi: { source_name: values.ndiSourceName.trim() } }
    case 'WEBCAM':
      return {
        ...base,
        type: 'WEBCAM',
        webcam: { device_id: values.webcamDeviceId.trim(), format: values.webcamFormat.trim() || null },
      }
    case 'SRT':
      // The Listener/Caller choice belongs in the URL query, not a field of its own — see srtUrl.ts.
      return {
        ...base,
        type: 'SRT',
        srt: { url: srtUrlForMode(values.srtUrl, values.srtMode), latency_ms: values.srtLatencyMs },
      }
    case 'IMAGE':
      return { ...base, type: 'IMAGE', image: { file_path: values.imageFilePath.trim() } }
    case 'HTML':
      return {
        ...base,
        type: 'HTML',
        html: {
          url: values.htmlUrl.trim(),
          width: values.htmlWidth,
          height: values.htmlHeight,
          is_local_file: values.htmlIsLocalFile,
          fps: values.htmlFps,
          css: values.htmlCss.trim() || null,
        },
      }
    case 'MIX':
      return {
        ...base,
        type: 'MIX',
        mix: {
          layers: values.mixLayers.map((layer) => ({ ...layer })),
          canvas_width: values.mixCanvasWidth,
          canvas_height: values.mixCanvasHeight,
        },
      }
  }
}

/**
 * Validates form values for the currently selected `type`; returns human-readable errors (empty = valid).
 * Mirrors the PC's `SourceDefinitionValidator` so a form that passes here is not bounced by the API.
 */
export function validateSourceFormValues(values: SourceFormValues): string[] {
  const errors: string[] = []
  if (!values.id.trim()) errors.push('IDを入力してください。')
  if (!values.name.trim()) errors.push('名前を入力してください。')

  switch (values.type) {
    case 'NDI':
      if (!values.ndiSourceName.trim()) errors.push('NDIソース名を入力してください。')
      break
    case 'WEBCAM':
      if (!values.webcamDeviceId.trim()) errors.push('デバイスを選択してください。')
      break
    case 'SRT':
      // A Listener needs no URL at all — it binds the default port — so only a Caller must have one.
      if (values.srtMode === 'caller' && !values.srtUrl.trim()) {
        errors.push('Caller モードでは接続先SRT URLを入力してください。')
      }
      if (!Number.isFinite(values.srtLatencyMs) || values.srtLatencyMs < 0) {
        errors.push('レイテンシ(ms)は0以上の数値を入力してください。')
      }
      break
    case 'IMAGE':
      if (!values.imageFilePath.trim()) errors.push('画像ファイルのパスを入力してください。')
      break
    case 'HTML':
      if (!values.htmlUrl.trim()) errors.push('URL（またはローカルHTMLのパス）を入力してください。')
      if (values.htmlWidth <= 0 || values.htmlHeight <= 0) errors.push('ページの幅/高さは正の値にしてください。')
      if (values.htmlFps <= 0) errors.push('FPSは正の値にしてください。')
      break
    case 'MIX':
      if (values.mixCanvasWidth <= 0 || values.mixCanvasHeight <= 0) {
        errors.push('キャンバスの幅/高さは正の値にしてください。')
      }
      if (values.mixLayers.length === 0) {
        errors.push('ミックスには最低1つのレイヤーが必要です。')
      }
      values.mixLayers.forEach((layer, index) => {
        if (!layer.source_id.trim()) {
          errors.push(`レイヤー${index + 1}のソースを選択してください。`)
        } else if (layer.source_id === values.id.trim()) {
          // A mix is a scene; containing itself is infinite recursion the PC rejects anyway.
          errors.push(`レイヤー${index + 1}がこのミックス自身を参照しています。`)
        }
        if (layer.width <= 0 || layer.height <= 0) {
          errors.push(`レイヤー${index + 1}の幅/高さは正の値にしてください。`)
        }
      })
      break
  }

  return errors
}
