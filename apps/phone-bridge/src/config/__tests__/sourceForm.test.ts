import { describe, expect, it } from 'vitest'
import { isSourceDefinition } from '../../protocol/types'
import {
  buildSourceDefinition,
  emptySourceFormValues,
  fullFrameMixLayer,
  sourceFormValuesFromDefinition,
  validateSourceFormValues,
} from '../sourceForm'

describe('buildSourceDefinition', () => {
  it('builds an NDI SourceDefinition carrying only its own config member', () => {
    const values = { ...emptySourceFormValues('NDI'), id: 'src-1', name: 'Cam 1', ndiSourceName: 'STUDIO (Cam1)' }
    const definition = buildSourceDefinition(values)

    expect(definition).toEqual({
      id: 'src-1',
      name: 'Cam 1',
      type: 'NDI',
      audio_mode: 'AFV',
      ndi: { source_name: 'STUDIO (Cam1)' },
    })
    expect(isSourceDefinition(definition)).toBe(true)
  })

  it('builds a WEBCAM SourceDefinition, treating a blank format as null', () => {
    const values = {
      ...emptySourceFormValues('WEBCAM'),
      id: 'src-2',
      name: 'Webcam',
      webcamDeviceId: 'dev0',
      webcamFormat: '',
    }
    const definition = buildSourceDefinition(values)

    expect(definition).toEqual({
      id: 'src-2',
      name: 'Webcam',
      type: 'WEBCAM',
      audio_mode: 'AFV',
      webcam: { device_id: 'dev0', format: null },
    })
    expect(isSourceDefinition(definition)).toBe(true)
  })

  it('writes the SRT mode into the URL query, binding the wildcard for a listener', () => {
    const definition = buildSourceDefinition({
      ...emptySourceFormValues('SRT'),
      id: 'src-3',
      name: 'Remote',
      srtMode: 'listener',
      srtUrl: 'srt://192.168.1.100:9000',
      srtLatencyMs: 40,
    })

    expect(definition).toEqual({
      id: 'src-3',
      name: 'Remote',
      type: 'SRT',
      audio_mode: 'AFV',
      srt: { url: 'srt://0.0.0.0:9000?mode=listener', latency_ms: 40 },
    })
  })

  it('builds IMAGE / HTML / MIX definitions', () => {
    const image = buildSourceDefinition({
      ...emptySourceFormValues('IMAGE'),
      id: 'src-img',
      name: 'Logo',
      imageFilePath: 'C:\\media\\logo.png',
      audioMode: 'OFF',
    })
    expect(image).toMatchObject({ type: 'IMAGE', image: { file_path: 'C:\\media\\logo.png' }, audio_mode: 'OFF' })

    const html = buildSourceDefinition({
      ...emptySourceFormValues('HTML'),
      id: 'src-html',
      name: 'L3',
      htmlUrl: 'https://example.test/l3',
    })
    expect(html).toMatchObject({
      type: 'HTML',
      html: { url: 'https://example.test/l3', width: 1920, height: 1080, fps: 30, is_local_file: false, css: null },
    })

    const mix = buildSourceDefinition({
      ...emptySourceFormValues('MIX'),
      id: 'src-mix',
      name: 'Split',
      mixLayers: [fullFrameMixLayer('src-1', 1920, 1080, 0)],
    })
    expect(mix).toMatchObject({ type: 'MIX', mix: { canvas_width: 1920, canvas_height: 1080 } })
    expect(isSourceDefinition(mix)).toBe(true)
  })
})

describe('sourceFormValuesFromDefinition', () => {
  it('round-trips through buildSourceDefinition', () => {
    const original = buildSourceDefinition({
      ...emptySourceFormValues('SRT'),
      id: 'src-4',
      name: 'Remote 2',
      srtMode: 'caller',
      srtUrl: 'srt://host:9000',
      srtLatencyMs: 80,
    })

    expect(buildSourceDefinition(sourceFormValuesFromDefinition(original))).toEqual(original)
  })

  it('recovers the mode the URL declares, so an edit does not silently flip it', () => {
    const listener = buildSourceDefinition({
      ...emptySourceFormValues('SRT'),
      id: 'src-5',
      name: 'Listener',
      srtMode: 'listener',
    })

    expect(sourceFormValuesFromDefinition(listener).srtMode).toBe('listener')
  })

  it('round-trips a MIX definition, layers included', () => {
    const original = buildSourceDefinition({
      ...emptySourceFormValues('MIX'),
      id: 'src-mix',
      name: 'Split',
      mixLayers: [fullFrameMixLayer('src-1', 960, 1080, 0), fullFrameMixLayer('src-2', 960, 1080, 1)],
    })

    expect(buildSourceDefinition(sourceFormValuesFromDefinition(original))).toEqual(original)
  })
})

describe('validateSourceFormValues', () => {
  it('requires id and name', () => {
    const errors = validateSourceFormValues(emptySourceFormValues('NDI'))
    expect(errors).toContain('IDを入力してください。')
    expect(errors).toContain('名前を入力してください。')
  })

  it('requires a URL for a Caller but not for a Listener, which binds the default port', () => {
    const caller = { ...emptySourceFormValues('SRT'), id: 'x', name: 'y', srtMode: 'caller' as const }
    expect(validateSourceFormValues(caller)).toContain('Caller モードでは接続先SRT URLを入力してください。')

    const listener = { ...emptySourceFormValues('SRT'), id: 'x', name: 'y', srtMode: 'listener' as const }
    expect(validateSourceFormValues(listener)).toEqual([])
  })

  it('rejects a negative latency', () => {
    const values = { ...emptySourceFormValues('SRT'), id: 'x', name: 'y', srtLatencyMs: -1 }
    expect(validateSourceFormValues(values)).toContain('レイテンシ(ms)は0以上の数値を入力してください。')
  })

  it('rejects a mix with no layers or one that contains itself', () => {
    const empty = { ...emptySourceFormValues('MIX'), id: 'mix-1', name: 'Mix' }
    expect(validateSourceFormValues(empty)).toContain('ミックスには最低1つのレイヤーが必要です。')

    const selfReferencing = {
      ...empty,
      mixLayers: [fullFrameMixLayer('mix-1', 1920, 1080, 0)],
    }
    expect(validateSourceFormValues(selfReferencing)).toContain('レイヤー1がこのミックス自身を参照しています。')
  })

  it('passes for a fully filled-in NDI form', () => {
    const values = { ...emptySourceFormValues('NDI'), id: 'x', name: 'y', ndiSourceName: 'STUDIO' }
    expect(validateSourceFormValues(values)).toEqual([])
  })
})
