import type { ButtonEvent } from '../protocol/types'
import { parseControllerLine, splitLines } from './parseControllerLine'

export type SerialLinkState =
  | 'idle'
  | 'unsupported'
  | 'connecting'
  | 'open'
  | 'reconnecting'
  | 'closed'

export interface SerialLinkOptions {
  /** Matches the CDC baud rate used by the firmware (P-002); TinyUSB CDC ignores the actual value but a rate must be supplied. */
  baudRate?: number
  reconnectBaseDelayMs?: number
  reconnectMaxDelayMs?: number
}

type Unsubscribe = () => void

/**
 * Web Serial client for the Pico 2W USB-OTG bridge (§2.1 of
 * docs/specs/phone-web-bridge.md). WebUSB is intentionally not implemented
 * as a fallback transport: the firmware exposes a CDC-ACM interface, which
 * Chromium-based browsers already surface through Web Serial, so a WebUSB
 * raw-bulk-transfer path would duplicate that without a real use case.
 * Browsers with WebUSB but no Web Serial support surface an explicit error
 * instead (see `getBrowserCapabilities` in `../lib/browserSupport`).
 */
export class SerialLink {
  private port: SerialPort | null = null
  private reader: ReadableStreamDefaultReader<string> | null = null
  private state: SerialLinkState
  private readonly stateListeners = new Set<(state: SerialLinkState) => void>()
  private readonly eventListeners = new Set<(event: ButtonEvent) => void>()
  private readonly errorListeners = new Set<(message: string) => void>()
  private lineBuffer = ''
  private reconnectAttempt = 0
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private closedByUser = false

  private readonly baudRate: number
  private readonly reconnectBaseDelayMs: number
  private readonly reconnectMaxDelayMs: number

  constructor(options: SerialLinkOptions = {}) {
    this.baudRate = options.baudRate ?? 115_200
    this.reconnectBaseDelayMs = options.reconnectBaseDelayMs ?? 500
    this.reconnectMaxDelayMs = options.reconnectMaxDelayMs ?? 10_000
    this.state = isWebSerialSupported() ? 'idle' : 'unsupported'
  }

  getState(): SerialLinkState {
    return this.state
  }

  onStateChange(listener: (state: SerialLinkState) => void): Unsubscribe {
    this.stateListeners.add(listener)
    return () => this.stateListeners.delete(listener)
  }

  onEvent(listener: (event: ButtonEvent) => void): Unsubscribe {
    this.eventListeners.add(listener)
    return () => this.eventListeners.delete(listener)
  }

  onError(listener: (message: string) => void): Unsubscribe {
    this.errorListeners.add(listener)
    return () => this.errorListeners.delete(listener)
  }

  /** Must be invoked from a user-gesture event handler (browser requirement for `requestPort`). */
  async requestConnect(): Promise<void> {
    if (this.state === 'unsupported') {
      this.emitError('このブラウザは Web Serial に対応していません。')
      return
    }

    this.closedByUser = false
    try {
      const port = await navigator.serial.requestPort()
      this.port = port
      port.addEventListener('disconnect', this.handlePortDisconnect)
      await this.openPort(port)
    } catch (error) {
      this.setState('closed')
      this.emitError(toErrorMessage(error))
    }
  }

  disconnect(): void {
    this.closedByUser = true
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    void this.closePort()
    this.setState('closed')
  }

  private async openPort(port: SerialPort): Promise<void> {
    this.setState(this.reconnectAttempt > 0 ? 'reconnecting' : 'connecting')

    try {
      await port.open({ baudRate: this.baudRate })
    } catch (error) {
      this.emitError(toErrorMessage(error))
      this.scheduleReconnect()
      return
    }

    this.reconnectAttempt = 0
    this.lineBuffer = ''
    this.setState('open')
    void this.readLoop(port)
  }

  private async readLoop(port: SerialPort): Promise<void> {
    if (!port.readable) return

    // TextDecoderStream's `writable` is typed as WritableStream<BufferSource>, which TS's
    // pipeThrough overload resolution doesn't accept for a ReadableStream<Uint8Array> source.
    const decoder = new TextDecoderStream() as unknown as TransformStream<Uint8Array, string>
    const reader = port.readable.pipeThrough(decoder).getReader()
    this.reader = reader
    try {
      for (;;) {
        const { value, done } = await reader.read()
        if (done) break
        if (value) this.handleChunk(value)
      }
    } catch (error) {
      this.emitError(toErrorMessage(error))
    } finally {
      reader.releaseLock()
      this.reader = null
    }

    if (!this.closedByUser) this.scheduleReconnect()
  }

  private handleChunk(chunk: string): void {
    const { lines, remainder } = splitLines(this.lineBuffer, chunk)
    this.lineBuffer = remainder

    for (const line of lines) {
      const event = parseControllerLine(line)
      if (event) {
        for (const listener of this.eventListeners) listener(event)
      } else if (line.trim() !== '') {
        this.emitError(`不正な受信データを破棄しました: ${line.slice(0, 80)}`)
      }
    }
  }

  private readonly handlePortDisconnect = (): void => {
    if (this.closedByUser) return
    this.scheduleReconnect()
  }

  private scheduleReconnect(): void {
    if (this.closedByUser || !this.port) {
      this.setState('closed')
      return
    }
    if (this.reconnectTimer !== null) return

    this.setState('reconnecting')
    const delay = Math.min(
      this.reconnectBaseDelayMs * 2 ** this.reconnectAttempt,
      this.reconnectMaxDelayMs,
    )
    this.reconnectAttempt += 1
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null
      const port = this.port
      if (port) void this.openPort(port)
    }, delay)
  }

  private async closePort(): Promise<void> {
    const port = this.port
    this.port = null

    if (this.reader) {
      try {
        await this.reader.cancel()
      } catch {
        // best-effort: the underlying port may already be gone
      }
      this.reader = null
    }

    if (port) {
      port.removeEventListener('disconnect', this.handlePortDisconnect)
      try {
        await port.close()
      } catch {
        // best-effort: closing an already-closed/unplugged port throws
      }
    }
  }

  private setState(state: SerialLinkState): void {
    this.state = state
    for (const listener of this.stateListeners) listener(state)
  }

  private emitError(message: string): void {
    for (const listener of this.errorListeners) listener(message)
  }
}

function isWebSerialSupported(): boolean {
  return typeof navigator !== 'undefined' && 'serial' in navigator
}

function toErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  return String(error)
}
