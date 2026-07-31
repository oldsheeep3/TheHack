import { describe, expect, it } from 'vitest'
import {
  isMultiviewCell,
  isMultiviewConfig,
  isOutputSink,
  isSourceDefinition,
  isSourceProtocol,
  isSourceStatus,
  isSourceType,
  isTallyState,
  isWsEnvelope,
  MAX_OUTPUTS,
  MAX_SINKS_PER_KIND,
  MAX_WEBCAM_OUTPUTS,
  maxOutputsOf,
  OUTPUT_KINDS,
  OUTPUT_SINKS,
  type ConfigChangeRequest,
  type ModulesConfig,
  type MultiviewConfig,
  type OutputsConfig,
  type PicoNetworkConfig,
  type ProgramRequest,
  type SourceDefinition,
  type SourceInfo,
  type TallyState,
  type WsEnvelope,
} from '../types'

describe('WsEnvelope (§4.1)', () => {
  it('round-trips through JSON and satisfies the type guard', () => {
    const envelope: WsEnvelope = {
      event: 'button_press',
      data: { controller_id: 'main', button_id: 3, timestamp: 1773663861000 },
    }

    const roundTripped: unknown = JSON.parse(JSON.stringify(envelope))

    expect(roundTripped).toEqual(envelope)
    expect(isWsEnvelope(roundTripped)).toBe(true)
  })

  it('rejects malformed payloads', () => {
    expect(isWsEnvelope({ event: 'button_press', data: { controller_id: 'main' } })).toBe(false)
    expect(isWsEnvelope(null)).toBe(false)
    expect(isWsEnvelope('not-json')).toBe(false)
  })
})

describe('ConfigChangeRequest (§4.2)', () => {
  it('round-trips with the spec example fields, including snake_case pip_settings', () => {
    const request: ConfigChangeRequest = {
      target_channel: 2,
      source_type: 'SRT',
      source_url: 'srt://192.168.1.100:9000?mode=caller',
      pip_settings: {
        enabled: true,
        x_position: 1420,
        y_position: 80,
        width: 480,
        height: 270,
        opacity: 1.0,
      },
    }

    const roundTripped: unknown = JSON.parse(JSON.stringify(request))

    expect(roundTripped).toEqual(request)
  })
})

describe('SourceInfo', () => {
  it('round-trips a full source entry, including the SourceDefinition-linking id/type', () => {
    const source: SourceInfo = {
      channel: 1,
      name: 'Main Camera',
      protocol: 'UVC',
      resolution: '1920x1080',
      status: 'Connected',
      id: 'src-webcam-1',
      type: 'WEBCAM',
    }

    expect(JSON.parse(JSON.stringify(source))).toEqual(source)
  })
})

describe('SourceProtocol / SourceStatus guards', () => {
  it('accept known values', () => {
    expect(isSourceProtocol('UVC')).toBe(true)
    expect(isSourceProtocol('NDI')).toBe(true)
    expect(isSourceProtocol('SRT')).toBe(true)
    expect(isSourceStatus('Connected')).toBe(true)
    expect(isSourceStatus('Disconnected')).toBe(true)
    expect(isSourceStatus('Error')).toBe(true)
  })

  it('reject unknown values', () => {
    expect(isSourceProtocol('HDMI')).toBe(false)
    expect(isSourceStatus('pending')).toBe(false)
  })
})

describe('SourceType guard (§4.2)', () => {
  it('accepts NDI/WEBCAM/SRT and rejects unknown values', () => {
    expect(isSourceType('NDI')).toBe(true)
    expect(isSourceType('WEBCAM')).toBe(true)
    expect(isSourceType('SRT')).toBe(true)
    expect(isSourceType('UVC')).toBe(false)
  })
})

describe('SourceDefinition (POST /api/v1/sources, §4.2)', () => {
  it('round-trips and satisfies the type guard for each discriminated variant', () => {
    const ndi: SourceDefinition = {
      id: 'src-ndi-cam1',
      name: 'Cam 1 (NDI)',
      type: 'NDI',
      ndi: { source_name: 'STUDIO (Cam1)' },
      webcam: null,
      srt: null,
    }
    const webcam: SourceDefinition = {
      id: 'src-webcam-1',
      name: 'USB Cam',
      type: 'WEBCAM',
      ndi: null,
      webcam: { device_id: 'dev0', format: '1920x1080@30' },
      srt: null,
    }
    const srt: SourceDefinition = {
      id: 'src-srt-1',
      name: 'ATEM SRT',
      type: 'SRT',
      ndi: null,
      webcam: null,
      srt: { url: 'srt://192.168.1.100:9000?mode=caller', latency_ms: 40 },
    }

    for (const definition of [ndi, webcam, srt]) {
      const roundTripped: unknown = JSON.parse(JSON.stringify(definition))
      expect(roundTripped).toEqual(definition)
      expect(isSourceDefinition(roundTripped)).toBe(true)
    }
  })

  it('rejects malformed payloads', () => {
    expect(isSourceDefinition(null)).toBe(false)
    expect(isSourceDefinition({ id: 'x', name: 'y', type: 'NDI', ndi: null, webcam: null, srt: null })).toBe(
      false,
    )
    expect(isSourceDefinition({ id: 'x', name: 'y', type: 'HDMI' })).toBe(false)
  })
})

describe('ProgramRequest (POST /api/v1/program, §4.2)', () => {
  it('round-trips the spec example', () => {
    const request: ProgramRequest = {
      bus: 'PGM1',
      layers: [
        {
          source_id: 'src-ndi-cam1',
          pip: {
            enabled: true,
            x_position: 0,
            y_position: 0,
            width: 1920,
            height: 1080,
            opacity: 1.0,
            z_order: 0,
            crop: null,
          },
        },
      ],
      take: false,
    }

    expect(JSON.parse(JSON.stringify(request))).toEqual(request)
  })
})

describe('MultiviewConfig (PUT /api/v1/multiview, §4.2)', () => {
  it('round-trips a 16-cell layout and satisfies the type guard', () => {
    const config: MultiviewConfig = {
      cells: [
        'PGM1',
        'PGM2',
        'PVW1',
        'PVW2',
        'SRC:src-ndi-cam1',
        'SRC:src-webcam-1',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
        'EMPTY',
      ],
    }

    const roundTripped: unknown = JSON.parse(JSON.stringify(config))
    expect(roundTripped).toEqual(config)
    expect(isMultiviewConfig(roundTripped)).toBe(true)
  })

  it('rejects the wrong cell count or an invalid cell value', () => {
    expect(isMultiviewConfig({ cells: ['PGM1'] })).toBe(false)
    expect(isMultiviewConfig({ cells: Array(16).fill('BOGUS') })).toBe(false)
  })

  it('isMultiviewCell accepts SRC:<id> but rejects a bare "SRC:"', () => {
    expect(isMultiviewCell('SRC:src-ndi-cam1')).toBe(true)
    expect(isMultiviewCell('SRC:')).toBe(false)
    expect(isMultiviewCell('EMPTY')).toBe(true)
    expect(isMultiviewCell('BOGUS')).toBe(false)
  })
})

describe('OutputsConfig (PUT /api/v1/outputs, §4.2)', () => {
  it('round-trips the spec example, including the HDMI-only and NDI-only fields', () => {
    const config: OutputsConfig = {
      outputs: [
        { sink: 'VCAM1', source: 'PGM1' },
        { sink: 'HDMI1', source: 'PGM1', display_id: 1, hide_cursor: true, fullscreen: true },
        { sink: 'HDMI2', source: 'PGM2', display_id: 2, hide_cursor: true, fullscreen: true },
        { sink: 'NDI3', source: 'PGM2', ndi_name: 'SWITCHER PGM3' },
      ],
    }

    expect(JSON.parse(JSON.stringify(config))).toEqual(config)
  })

  it('isOutputSink accepts every ordinal token and rejects the ordinal-less legacy ones', () => {
    // Nine tokens, which is not nine addable sinks: VCAM2/VCAM3 stay parseable only so tables written
    // by builds that predate the one-webcam limit still load.
    expect(OUTPUT_SINKS).toHaveLength(9)
    for (const sink of OUTPUT_SINKS) {
      expect(isOutputSink(sink)).toBe(true)
    }
    expect(isOutputSink('HDMI')).toBe(false)
    expect(isOutputSink('VCAM')).toBe(false)
    expect(isOutputSink('HDMI4')).toBe(false)
  })

  it('maxOutputsOf caps webcams at one and every other kind at three', () => {
    expect(maxOutputsOf('WEBCAM')).toBe(MAX_WEBCAM_OUTPUTS)
    expect(maxOutputsOf('WEBCAM')).toBe(1)
    expect(maxOutputsOf('HDMI')).toBe(MAX_SINKS_PER_KIND)
    expect(maxOutputsOf('NDI')).toBe(MAX_SINKS_PER_KIND)
    // The per-kind ceilings add up to more than one table may hold, so MAX_OUTPUTS still binds.
    expect(OUTPUT_KINDS.reduce((total, kind) => total + maxOutputsOf(kind), 0)).toBeGreaterThan(MAX_OUTPUTS)
  })
})

describe('ModulesConfig (PUT /api/v1/modules, §4.2)', () => {
  it('round-trips the spec example', () => {
    const config: ModulesConfig = {
      modules: [
        {
          index: 0,
          src1: { source_id: 'src-ndi-cam1', vr_target: 'transition' },
          src2: { source_id: 'src-webcam-1', vr_target: 'opacity' },
        },
      ],
    }

    expect(JSON.parse(JSON.stringify(config))).toEqual(config)
  })
})

describe('PicoNetworkConfig (PUT /api/v1/pico/network, §4.6)', () => {
  it('round-trips Wi-Fi/BT credential fields', () => {
    const config: PicoNetworkConfig = {
      wifi_ssid: 'studio-lan',
      wifi_password: 'hunter2',
      controller_id: 'main',
      bluetooth_enabled: false,
    }

    expect(JSON.parse(JSON.stringify(config))).toEqual(config)
  })
})

describe('TallyState (§4.3, 2-bus)', () => {
  it('round-trips the 2-bus UDP broadcast payload shape', () => {
    const tally: TallyState = { active_pgm1: [1, 3], active_pgm2: [2], active_pvw1: [4], active_pvw2: [] }
    const roundTripped: unknown = JSON.parse(JSON.stringify(tally))
    expect(roundTripped).toEqual(tally)
    expect(isTallyState(roundTripped)).toBe(true)
  })

  it('accepts single-bus deployments that leave PGM2/PVW2 empty', () => {
    expect(isTallyState({ active_pgm1: [1], active_pgm2: [], active_pvw1: [2], active_pvw2: [] })).toBe(true)
  })

  it('rejects the old single-bus shape and malformed payloads', () => {
    expect(isTallyState({ active_pgm: [1], active_pvw: [2] })).toBe(false)
    expect(isTallyState(null)).toBe(false)
  })
})
