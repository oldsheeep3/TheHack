import { afterEach, describe, expect, it } from 'vitest'
import { DEFAULT_PORT, defaultHostConfig, loadHostConfig, saveHostConfig } from '../hostConfig'

afterEach(() => {
  window.localStorage.clear()
})

describe('hostConfig', () => {
  it('returns null when nothing has been saved', () => {
    expect(loadHostConfig()).toBeNull()
  })

  it('defaults to the origin this page was served from, since the PC serves the UI itself', () => {
    // Following the page's own port is what keeps a non-default WebPort working; falling back to
    // DEFAULT_PORT only covers a URL with no port at all (http://host/).
    expect(defaultHostConfig()).toEqual({
      host: window.location.hostname,
      port: Number(window.location.port) || DEFAULT_PORT,
    })
    expect(defaultHostConfig().port).toBe(Number(window.location.port))
  })

  it('round-trips a saved host/port through localStorage', () => {
    saveHostConfig({ host: '192.168.1.50', port: DEFAULT_PORT })
    expect(loadHostConfig()).toEqual({ host: '192.168.1.50', port: DEFAULT_PORT })
  })

  it('accepts a manually-entered mDNS hostname', () => {
    saveHostConfig({ host: 'switcher-pc.local', port: 8080 })
    expect(loadHostConfig()).toEqual({ host: 'switcher-pc.local', port: 8080 })
  })

  it('discards malformed stored values', () => {
    window.localStorage.setItem('phone-bridge.pc-host', JSON.stringify({ host: '' , port: 8080 }))
    expect(loadHostConfig()).toBeNull()

    window.localStorage.setItem('phone-bridge.pc-host', 'not-json')
    expect(loadHostConfig()).toBeNull()
  })
})
