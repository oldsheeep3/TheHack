import { describe, expect, it } from 'vitest'
import { isSourceDefinition } from '../../protocol/types'
import {
  buildSourceDefinition,
  emptySourceFormValues,
  sourceFormValuesFromDefinition,
  validateSourceFormValues,
} from '../sourceForm'

describe('buildSourceDefinition', () => {
  it('builds an NDI SourceDefinition, nulling the other variants', () => {
    const values = { ...emptySourceFormValues('NDI'), id: 'src-1', name: 'Cam 1', ndiSourceName: 'STUDIO (Cam1)' }
    const definition = buildSourceDefinition(values)

    expect(definition).toEqual({
      id: 'src-1',
      name: 'Cam 1',
      type: 'NDI',
      ndi: { source_name: 'STUDIO (Cam1)' },
      webcam: null,
      srt: null,
    })
    expect(isSourceDefinition(definition)).toBe(true)
  })

  it('builds a WEBCAM SourceDefinition, treating a blank format as null', () => {
    const values = {
      ...emptySourceFormValues('WEBCAM'),
      id: 'src-2',
      name: 'Webcam',
      webcamDeviceId: '/dev/video0',
      webcamFormat: '',
    }
    const definition = buildSourceDefinition(values)

    expect(definition).toEqual({
      id: 'src-2',
      name: 'Webcam',
      type: 'WEBCAM',
      ndi: null,
      webcam: { device_id: '/dev/video0', format: null },
      srt: null,
    })
    expect(isSourceDefinition(definition)).toBe(true)
  })

  it('builds an SRT SourceDefinition', () => {
    const values = {
      ...emptySourceFormValues('SRT'),
      id: 'src-3',
      name: 'Remote',
      srtUrl: 'srt://192.168.1.100:9000?mode=caller',
      srtLatencyMs: 40,
    }
    const definition = buildSourceDefinition(values)

    expect(definition).toEqual({
      id: 'src-3',
      name: 'Remote',
      type: 'SRT',
      ndi: null,
      webcam: null,
      srt: { url: 'srt://192.168.1.100:9000?mode=caller', latency_ms: 40 },
    })
    expect(isSourceDefinition(definition)).toBe(true)
  })
})

describe('sourceFormValuesFromDefinition', () => {
  it('round-trips through buildSourceDefinition', () => {
    const original = buildSourceDefinition({
      ...emptySourceFormValues('SRT'),
      id: 'src-4',
      name: 'Remote 2',
      srtUrl: 'srt://host:9000',
      srtLatencyMs: 80,
    })
    const values = sourceFormValuesFromDefinition(original)
    expect(buildSourceDefinition(values)).toEqual(original)
  })
})

describe('validateSourceFormValues', () => {
  it('requires id and name', () => {
    const errors = validateSourceFormValues(emptySourceFormValues('NDI'))
    expect(errors).toContain('IDを入力してください。')
    expect(errors).toContain('名前を入力してください。')
  })

  it('requires type-specific fields', () => {
    const values = { ...emptySourceFormValues('SRT'), id: 'x', name: 'y', srtLatencyMs: -1 }
    const errors = validateSourceFormValues(values)
    expect(errors).toContain('SRT URLを入力してください。')
    expect(errors).toContain('レイテンシ(ms)は0以上の数値を入力してください。')
  })

  it('passes for a fully filled-in NDI form', () => {
    const values = { ...emptySourceFormValues('NDI'), id: 'x', name: 'y', ndiSourceName: 'STUDIO' }
    expect(validateSourceFormValues(values)).toEqual([])
  })
})
