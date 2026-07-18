import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { WsClient, type WsConnectionState } from '../wsClient'

/** Minimal fake of the `WebSocket` surface WsClient relies on. */
class FakeWebSocket {
  static readonly CONNECTING = 0
  static readonly OPEN = 1
  static readonly CLOSING = 2
  static readonly CLOSED = 3
  static instances: FakeWebSocket[] = []

  readyState = FakeWebSocket.CONNECTING
  onopen: (() => void) | null = null
  onclose: (() => void) | null = null
  onerror: (() => void) | null = null
  onmessage: ((event: { data: string }) => void) | null = null
  url: string

  constructor(url: string) {
    this.url = url
    FakeWebSocket.instances.push(this)
  }

  open(): void {
    this.readyState = FakeWebSocket.OPEN
    this.onopen?.()
  }

  /** Simulates the remote end (or network) closing the connection. */
  remoteClose(): void {
    this.readyState = FakeWebSocket.CLOSED
    this.onclose?.()
  }

  send(): void {}

  close(): void {
    this.readyState = FakeWebSocket.CLOSED
    this.onclose?.()
  }
}

beforeEach(() => {
  FakeWebSocket.instances = []
  vi.stubGlobal('WebSocket', FakeWebSocket)
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('WsClient', () => {
  it('transitions idle -> connecting -> open', () => {
    const client = new WsClient({ url: 'ws://pc.local:8080/ws' })
    const states: WsConnectionState[] = []
    client.onStateChange((state) => states.push(state))

    client.connect()
    expect(states).toEqual(['connecting'])

    FakeWebSocket.instances[0].open()
    expect(states).toEqual(['connecting', 'open'])

    client.disconnect()
  })

  it('reconnects with exponential backoff across consecutive failures, then resets after success (mock clock)', () => {
    const client = new WsClient({ url: 'ws://pc.local:8080/ws', reconnectBaseDelayMs: 100, reconnectMaxDelayMs: 1000 })
    const states: WsConnectionState[] = []
    client.onStateChange((state) => states.push(state))

    client.connect()
    FakeWebSocket.instances[0].open()
    expect(FakeWebSocket.instances).toHaveLength(1)

    FakeWebSocket.instances[0].remoteClose()
    expect(states.at(-1)).toBe('reconnecting')

    vi.advanceTimersByTime(99)
    expect(FakeWebSocket.instances).toHaveLength(1) // not yet due

    vi.advanceTimersByTime(1)
    expect(FakeWebSocket.instances).toHaveLength(2) // first retry at base delay (100ms)

    // A second consecutive failure (no successful open in between) doubles the delay.
    FakeWebSocket.instances[1].remoteClose()
    vi.advanceTimersByTime(199)
    expect(FakeWebSocket.instances).toHaveLength(2)

    vi.advanceTimersByTime(1)
    expect(FakeWebSocket.instances).toHaveLength(3) // second retry doubles to 200ms

    // Once a reconnect succeeds, the backoff resets to the base delay again.
    FakeWebSocket.instances[2].open()
    expect(states.at(-1)).toBe('open')

    FakeWebSocket.instances[2].remoteClose()
    vi.advanceTimersByTime(99)
    expect(FakeWebSocket.instances).toHaveLength(3)

    vi.advanceTimersByTime(1)
    expect(FakeWebSocket.instances).toHaveLength(4) // back to base delay (100ms), not maxed out

    client.disconnect()
  })

  it('does not reconnect after a user-initiated disconnect', () => {
    const client = new WsClient({ url: 'ws://pc.local:8080/ws', reconnectBaseDelayMs: 50 })
    client.connect()
    FakeWebSocket.instances[0].open()

    client.disconnect()
    expect(client.getState()).toBe('closed')

    vi.advanceTimersByTime(10_000)
    expect(FakeWebSocket.instances).toHaveLength(1)
  })
})
