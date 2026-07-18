import { afterEach, describe, expect, it } from 'vitest'
import { DEFAULT_PORT, loadHostConfig, saveHostConfig } from '../hostConfig'

afterEach(() => {
  window.localStorage.clear()
})

describe('hostConfig', () => {
  it('returns null when nothing has been saved', () => {
    expect(loadHostConfig()).toBeNull()
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
