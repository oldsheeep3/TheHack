import { describe, expect, it } from 'vitest'
import { SRT_LISTEN_PORT, srtModeOf, srtUrlForMode } from '../srtUrl'

describe('srtUrlForMode', () => {
  it('binds the wildcard for a listener, keeping only the port', () => {
    expect(srtUrlForMode('srt://192.168.1.50:9001', 'listener')).toBe('srt://0.0.0.0:9001?mode=listener')
  })

  it('falls back to the protocol listener port when no URL is supplied', () => {
    expect(srtUrlForMode('', 'listener')).toBe(`srt://0.0.0.0:${SRT_LISTEN_PORT}?mode=listener`)
  })

  it('keeps the address for a caller and appends the mode libsrt would otherwise assume', () => {
    expect(srtUrlForMode('srt://192.168.1.100:9000', 'caller')).toBe('srt://192.168.1.100:9000?mode=caller')
  })

  it('accepts a bare host:port', () => {
    expect(srtUrlForMode('192.168.1.100:9000', 'caller')).toBe('srt://192.168.1.100:9000?mode=caller')
  })

  it('preserves an explicit mode the operator typed, and any other query parameter', () => {
    expect(srtUrlForMode('srt://host:9000?passphrase=secret', 'caller')).toBe(
      'srt://host:9000?passphrase=secret&mode=caller',
    )
    expect(srtUrlForMode('srt://host:9000?mode=rendezvous', 'caller')).toBe('srt://host:9000?mode=rendezvous')
  })
})

describe('srtModeOf', () => {
  it('reads the mode out of the query, defaulting to caller the way libsrt does', () => {
    expect(srtModeOf('srt://0.0.0.0:9000?mode=listener')).toBe('listener')
    expect(srtModeOf('srt://host:9000?mode=caller')).toBe('caller')
    expect(srtModeOf('srt://host:9000')).toBe('caller')
    expect(srtModeOf(null)).toBe('caller')
  })
})
