import { afterEach, describe, expect, it } from 'vitest'
import type { ButtonEvent } from '../../protocol/types'
import { SerialLink, type SerialLinkState } from '../serialLink'

/** Minimal fake of the Web Serial `SerialPort` surface SerialLink relies on. */
class FakeSerialPort extends EventTarget {
  connected = true
  readable: ReadableStream<Uint8Array> | null = null
  writable: WritableStream<Uint8Array> | null = null
  openCalls = 0
  failOpensRemaining = 0
  private controller: ReadableStreamDefaultController<Uint8Array> | null = null

  open(): Promise<void> {
    this.openCalls += 1
    if (this.failOpensRemaining > 0) {
      this.failOpensRemaining -= 1
      return Promise.reject(new Error('mock open failure'))
    }
    this.readable = new ReadableStream<Uint8Array>({
      start: (controller) => {
        this.controller = controller
      },
    })
    return Promise.resolve()
  }

  close(): Promise<void> {
    this.controller?.close()
    this.controller = null
    this.readable = null
    return Promise.resolve()
  }

  setSignals(): Promise<void> {
    return Promise.resolve()
  }

  getSignals(): Promise<SerialInputSignals> {
    return Promise.resolve({
      dataCarrierDetect: false,
      clearToSend: false,
      ringIndicator: false,
      dataSetReady: false,
    })
  }

  getInfo(): SerialPortInfo {
    return {}
  }

  forget(): Promise<void> {
    return Promise.resolve()
  }

  pushLine(line: string): void {
    this.controller?.enqueue(new TextEncoder().encode(line))
  }
}

function installFakeSerial(port: FakeSerialPort): void {
  Object.defineProperty(navigator, 'serial', {
    value: {
      requestPort: () => Promise.resolve(port as unknown as SerialPort),
      getPorts: () => Promise.resolve([port as unknown as SerialPort]),
    },
    configurable: true,
  })
}

function tick(ms = 0): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'serial')
})

describe('SerialLink', () => {
  it('reports "unsupported" and emits an error when navigator.serial is absent', async () => {
    Reflect.deleteProperty(navigator, 'serial')
    const link = new SerialLink()
    expect(link.getState()).toBe('unsupported')

    const errors: string[] = []
    link.onError((message) => errors.push(message))
    await link.requestConnect()
    expect(errors).toHaveLength(1)
  })

  it('connects, parses incoming lines, and reconnects after the device disconnects', async () => {
    const port = new FakeSerialPort()
    installFakeSerial(port)

    const link = new SerialLink({ reconnectBaseDelayMs: 15, reconnectMaxDelayMs: 200 })
    const states: SerialLinkState[] = []
    link.onStateChange((state) => states.push(state))
    const events: ButtonEvent[] = []
    link.onEvent((event) => events.push(event))

    await link.requestConnect()
    expect(states).toEqual(['connecting', 'open'])
    expect(port.openCalls).toBe(1)

    port.pushLine('{"event":"button_press","data":{"controller_id":"main","button_id":5,"timestamp":123}}\n')
    await tick(10)
    expect(events).toEqual([{ controller_id: 'main', button_id: 5, timestamp: 123 }])

    port.dispatchEvent(new Event('disconnect'))
    await tick(5)
    expect(states.at(-1)).toBe('reconnecting')

    await tick(60)
    expect(port.openCalls).toBe(2)
    expect(states.at(-1)).toBe('open')

    link.disconnect()
    await tick(10)
    expect(states.at(-1)).toBe('closed')
  })

  it('keeps retrying with backoff when reopening the port fails', async () => {
    const port = new FakeSerialPort()
    installFakeSerial(port)

    const link = new SerialLink({ reconnectBaseDelayMs: 15, reconnectMaxDelayMs: 200 })
    const states: SerialLinkState[] = []
    link.onStateChange((state) => states.push(state))

    await link.requestConnect()
    expect(port.openCalls).toBe(1)

    port.failOpensRemaining = 1
    port.dispatchEvent(new Event('disconnect'))

    await tick(30)
    expect(port.openCalls).toBe(2) // first reopen attempt fails
    expect(states.at(-1)).toBe('reconnecting')

    await tick(60)
    expect(port.openCalls).toBe(3) // second reopen attempt succeeds
    expect(states.at(-1)).toBe('open')

    link.disconnect()
  })
})
