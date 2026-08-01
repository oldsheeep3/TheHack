import { describe, expect, it } from 'vitest'
import {
  isLegacyMultiviewCells,
  isMultiviewCell,
  isMultiviewRegion,
  isOutputSink,
  isSourceAudioMode,
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
  outputKindOf,
  OUTPUT_KINDS,
  OUTPUT_SINKS,
  type AtemConfig,
  type AudioOutputsConfig,
  type ConfigChangeRequest,
  type ModulesConfig,
  type MultiviewLayout,
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
  it('round-trips the runtime view the PC serves, which carries an order rather than a type', () => {
    const source: SourceInfo = {
      channel: 1,
      name: 'Main Camera',
      protocol: 'UVC',
      resolution: '1920x1080',
      status: 'Connected',
      id: 'src-webcam-1',
      order: 0,
    }

    expect(JSON.parse(JSON.stringify(source))).toEqual(source)
  })
})

describe('SourceProtocol / SourceStatus guards', () => {
  it('accept known values, including the libobs-era additions', () => {
    expect(isSourceProtocol('UVC')).toBe(true)
    expect(isSourceProtocol('NDI')).toBe(true)
    expect(isSourceProtocol('SRT')).toBe(true)
    expect(isSourceProtocol('IMAGE')).toBe(true)
    expect(isSourceProtocol('HTML')).toBe(true)
    expect(isSourceProtocol('MIX')).toBe(true)
    expect(isSourceStatus('Connected')).toBe(true)
    expect(isSourceStatus('Disconnected')).toBe(true)
    expect(isSourceStatus('Error')).toBe(true)
  })

  it('reject unknown values', () => {
    expect(isSourceProtocol('HDMI')).toBe(false)
    expect(isSourceStatus('pending')).toBe(false)
  })
})

describe('SourceType / SourceAudioMode guards (§4.2)', () => {
  it('accept every type the PC supports and reject unknown values', () => {
    for (const type of ['NDI', 'WEBCAM', 'SRT', 'IMAGE', 'HTML', 'MIX']) {
      expect(isSourceType(type)).toBe(true)
    }
    expect(isSourceType('UVC')).toBe(false)
  })

  it('accept OFF/ON/AFV', () => {
    expect(isSourceAudioMode('AFV')).toBe(true)
    expect(isSourceAudioMode('ON')).toBe(true)
    expect(isSourceAudioMode('OFF')).toBe(true)
    expect(isSourceAudioMode('MUTE')).toBe(false)
  })
})

describe('SourceDefinition (POST /api/v1/sources, §4.2)', () => {
  it('round-trips and satisfies the type guard for each variant', () => {
    const definitions: SourceDefinition[] = [
      {
        id: 'src-ndi-cam1',
        name: 'Cam 1 (NDI)',
        type: 'NDI',
        ndi: { source_name: 'STUDIO (Cam1)' },
        audio_mode: 'AFV',
      },
      {
        id: 'src-webcam-1',
        name: 'USB Cam',
        type: 'WEBCAM',
        webcam: { device_id: 'dev0', format: '1920x1080@30' },
        audio_mode: 'AFV',
      },
      {
        id: 'src-srt-1',
        name: 'ATEM SRT',
        type: 'SRT',
        srt: { url: 'srt://0.0.0.0:9000?mode=listener', latency_ms: 40 },
        audio_mode: 'AFV',
      },
      {
        id: 'src-image-1',
        name: 'Logo',
        type: 'IMAGE',
        image: { file_path: 'C:\\media\\logo.png' },
        audio_mode: 'OFF',
      },
      {
        id: 'src-html-1',
        name: 'Lower third',
        type: 'HTML',
        html: { url: 'https://example.test/l3', width: 1920, height: 1080, is_local_file: false, fps: 30, css: null },
        audio_mode: 'ON',
      },
      {
        id: 'src-mix-1',
        name: 'Split screen',
        type: 'MIX',
        mix: {
          layers: [
            {
              source_id: 'src-ndi-cam1',
              x_position: 0,
              y_position: 0,
              width: 960,
              height: 1080,
              z_order: 0,
              crop: null,
            },
          ],
          canvas_width: 1920,
          canvas_height: 1080,
        },
        audio_mode: 'AFV',
      },
    ]

    for (const definition of definitions) {
      const roundTripped: unknown = JSON.parse(JSON.stringify(definition))
      expect(roundTripped).toEqual(definition)
      expect(isSourceDefinition(roundTripped)).toBe(true)
    }
  })

  it('rejects malformed payloads', () => {
    expect(isSourceDefinition(null)).toBe(false)
    expect(isSourceDefinition({ id: 'x', name: 'y', type: 'NDI' })).toBe(false)
    expect(isSourceDefinition({ id: 'x', name: 'y', type: 'MIX', mix: { layers: [] } })).toBe(false)
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

describe('MultiviewLayout (PUT /api/v1/multiview, §4.2)', () => {
  it('round-trips the region form, spans included', () => {
    const layout: MultiviewLayout = {
      grid: { rows: 4, cols: 4 },
      regions: [{ row: 0, col: 0, row_span: 2, col_span: 2, content: 'PGM1' }],
    }

    const roundTripped: unknown = JSON.parse(JSON.stringify(layout))
    expect(roundTripped).toEqual(layout)
    expect(isMultiviewRegion(layout.regions?.[0])).toBe(true)
  })

  it('recognizes the legacy 16-cell form an older PC still serves', () => {
    const cells = Array.from({ length: 16 }, () => 'EMPTY')
    expect(isLegacyMultiviewCells({ cells })).toBe(true)
    expect(isLegacyMultiviewCells({ cells: ['PGM1'] })).toBe(false)
    expect(isLegacyMultiviewCells({ cells: Array(16).fill('BOGUS') })).toBe(false)
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

  it('outputKindOf groups each sink under its kind', () => {
    expect(outputKindOf('VCAM1')).toBe('WEBCAM')
    expect(outputKindOf('HDMI3')).toBe('HDMI')
    expect(outputKindOf('NDI2')).toBe('NDI')
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

describe('AudioOutputsConfig (PUT /api/v1/audio/outputs, §4.2)', () => {
  it('round-trips a table routing one bus to two devices', () => {
    const config: AudioOutputsConfig = {
      outputs: [
        { bus: 'PGM1', device_id: '', device_name: null },
        { bus: 'PGM1', device_id: '{0.0.0.1}', device_name: 'Speakers' },
      ],
    }

    expect(JSON.parse(JSON.stringify(config))).toEqual(config)
  })
})

describe('AtemConfig (PUT /api/v1/atem, §4.2)', () => {
  it('round-trips the spec example mapping', () => {
    const config: AtemConfig = {
      enabled: true,
      ip: '192.168.1.240',
      name: 'ATEM Mini Pro',
      mappings: [
        {
          controller_id: 'hid',
          module_index: 0,
          switch: 'Pgm1Src1',
          action: 'ProgramInput',
          mix_effect: 0,
          source: 1,
        },
      ],
    }

    expect(JSON.parse(JSON.stringify(config))).toEqual(config)
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

  it('rejects the old single-bus shape and malformed payloads', () => {
    expect(isTallyState({ active_pgm: [1], active_pvw: [2] })).toBe(false)
    expect(isTallyState(null)).toBe(false)
  })
})
