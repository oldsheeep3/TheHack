/**
 * Pure conversion between a flat, form-friendly value bag and the
 * `SourceDefinition` discriminated union (§4.2). Kept free of React so the
 * type-out logic and validation are unit testable without a rendered form.
 */
import type { SourceDefinition, SourceType } from '../protocol/types'

export interface SourceFormValues {
  id: string
  name: string
  type: SourceType
  ndiSourceName: string
  webcamDeviceId: string
  webcamFormat: string
  srtUrl: string
  srtLatencyMs: number
}

export function emptySourceFormValues(type: SourceType = 'NDI'): SourceFormValues {
  return {
    id: '',
    name: '',
    type,
    ndiSourceName: '',
    webcamDeviceId: '',
    webcamFormat: '',
    srtUrl: '',
    srtLatencyMs: 40,
  }
}

export function sourceFormValuesFromDefinition(source: SourceDefinition): SourceFormValues {
  return {
    id: source.id,
    name: source.name,
    type: source.type,
    ndiSourceName: source.ndi?.source_name ?? '',
    webcamDeviceId: source.webcam?.device_id ?? '',
    webcamFormat: source.webcam?.format ?? '',
    srtUrl: source.srt?.url ?? '',
    srtLatencyMs: source.srt?.latency_ms ?? 40,
  }
}

/** Builds the `SourceDefinition` discriminated union to send to the source CRUD endpoints. */
export function buildSourceDefinition(values: SourceFormValues): SourceDefinition {
  const base = { id: values.id, name: values.name }
  switch (values.type) {
    case 'NDI':
      return { ...base, type: 'NDI', ndi: { source_name: values.ndiSourceName }, webcam: null, srt: null }
    case 'WEBCAM':
      return {
        ...base,
        type: 'WEBCAM',
        ndi: null,
        webcam: { device_id: values.webcamDeviceId, format: values.webcamFormat || null },
        srt: null,
      }
    case 'SRT':
      return {
        ...base,
        type: 'SRT',
        ndi: null,
        webcam: null,
        srt: { url: values.srtUrl, latency_ms: values.srtLatencyMs },
      }
  }
}

/** Validates form values for the currently selected `type`; returns human-readable errors (empty = valid). */
export function validateSourceFormValues(values: SourceFormValues): string[] {
  const errors: string[] = []
  if (!values.id.trim()) errors.push('IDを入力してください。')
  if (!values.name.trim()) errors.push('名前を入力してください。')
  switch (values.type) {
    case 'NDI':
      if (!values.ndiSourceName.trim()) errors.push('NDIソース名を入力してください。')
      break
    case 'WEBCAM':
      if (!values.webcamDeviceId.trim()) errors.push('デバイスIDを入力してください。')
      break
    case 'SRT':
      if (!values.srtUrl.trim()) errors.push('SRT URLを入力してください。')
      if (!Number.isFinite(values.srtLatencyMs) || values.srtLatencyMs < 0) {
        errors.push('レイテンシ(ms)は0以上の数値を入力してください。')
      }
      break
  }
  return errors
}
