/**
 * Pure parsing helpers for the Pico 2W CDC serial stream (§4.1 of
 * docs/specs/00-system-overview.md). The firmware (P-002) writes one
 * `WsEnvelope` JSON object per line; these functions are deliberately kept
 * free of any I/O so they can be unit tested without a real SerialPort.
 */

import { isWsEnvelope, type ButtonEvent } from '../protocol/types'

/** Parses a single already-newline-delimited line into a `ButtonEvent`, or `null` if malformed/empty. */
export function parseControllerLine(line: string): ButtonEvent | null {
  const trimmed = line.trim()
  if (trimmed === '') return null

  let parsed: unknown
  try {
    parsed = JSON.parse(trimmed)
  } catch {
    return null
  }

  if (!isWsEnvelope(parsed)) return null
  return parsed.data
}

export interface LineSplitResult {
  /** Complete, newline-terminated lines extracted from `buffered + chunk`. */
  lines: string[]
  /** Trailing partial line to prepend to the next chunk. */
  remainder: string
}

/**
 * Splits a stream chunk into complete lines, carrying over any trailing
 * partial line via `remainder`. Handles both `\n` and `\r\n` line endings
 * (the `\r` is stripped by `parseControllerLine`'s trim).
 */
export function splitLines(buffered: string, chunk: string): LineSplitResult {
  const combined = buffered + chunk
  const parts = combined.split('\n')
  const remainder = parts.pop() ?? ''
  return { lines: parts, remainder }
}
