import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConnectionManager } from '../connectionManager'
import { loadHostConfig } from '../hostConfig'

/** Minimal fake of the `WebSocket` surface WsClient relies on. */
class FakeWebSocket {
  static readonly OPEN = 1
  static readonly CLOSED = 3
  static instances: FakeWebSocket[] = []

  readyState = 0
  onopen: (() => void) | null = null
  onclose: (() => void) | null = null
  onerror: (() => void) | null = null
  onmessage: ((event: { data: string }) => void) | null = null
  url: string

  constructor(url: string) {
    this.url = url
    FakeWebSocket.instances.push(this)
  }

  send(): void {}

  close(): void {
    this.readyState = FakeWebSocket.CLOSED
    this.onclose?.()
  }
}

function stubOkFetch(): void {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve([]) }),
  )
}

beforeEach(() => {
  window.localStorage.clear()
  FakeWebSocket.instances = []
  vi.stubGlobal('WebSocket', FakeWebSocket)
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  window.localStorage.clear()
})

describe('ConnectionManager', () => {
  it('polls reachability via GET /api/v1/sources and reports reachable', async () => {
    stubOkFetch()

    const manager = new ConnectionManager({ reachabilityPollMs: 1000 })
    const states: string[] = []
    manager.onReachabilityChange((state) => states.push(state))

    manager.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(states).toEqual(['checking', 'reachable'])
    manager.stop()
  })

  it('reports unreachable when the REST poll fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network error')))

    const manager = new ConnectionManager({ reachabilityPollMs: 1000 })
    const states: string[] = []
    manager.onReachabilityChange((state) => states.push(state))

    manager.start()
    await vi.advanceTimersByTimeAsync(0)

    expect(states).toEqual(['checking', 'unreachable'])
    manager.stop()
  })

  it('persists a new host/port, reconfigures the ApiClient baseUrl, and recreates the WsClient', () => {
    stubOkFetch()

    const manager = new ConnectionManager()
    manager.start()
    const wsCountBefore = FakeWebSocket.instances.length

    manager.setHost('192.168.1.77', 9090)

    expect(manager.getHost()).toBe('192.168.1.77')
    expect(manager.getPort()).toBe(9090)
    expect(manager.getApiClient().getBaseUrl()).toBe('http://192.168.1.77:9090')
    expect(FakeWebSocket.instances.length).toBeGreaterThan(wsCountBefore)
    expect(loadHostConfig()).toEqual({ host: '192.168.1.77', port: 9090 })

    manager.stop()
  })

  it('stop() unsubscribes and clears reachability back to unknown', async () => {
    stubOkFetch()

    const manager = new ConnectionManager({ reachabilityPollMs: 1000 })
    manager.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(manager.getReachability()).toBe('reachable')

    manager.stop()
    expect(manager.getReachability()).toBe('unknown')

    await vi.advanceTimersByTimeAsync(5000)
    expect(manager.getReachability()).toBe('unknown')
  })
})
