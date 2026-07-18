import { describe, expect, it } from 'vitest'
import {
  isSourceProtocol,
  isSourceStatus,
  isWsEnvelope,
  type ConfigChangeRequest,
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
  it('round-trips a full source entry', () => {
    const source: SourceInfo = {
      channel: 1,
      name: 'Main Camera',
      protocol: 'UVC',
      resolution: '1920x1080',
      status: 'Connected',
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

describe('TallyState (§4.3)', () => {
  it('round-trips the UDP broadcast payload shape', () => {
    const tally: TallyState = { active_pgm: [1, 5], active_pvw: [2] }
    expect(JSON.parse(JSON.stringify(tally))).toEqual(tally)
  })
})
