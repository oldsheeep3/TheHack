/**
 * Builds the `srt://` URL the PC opens for an SRT source, mirroring `Switcher.Contracts.SrtUrl`.
 *
 * `SrtSourceConfig` carries no mode field: the Listener/Caller choice belongs in the URL query (§4.3),
 * and FFmpeg's libsrt defaults to `caller` when it is absent. That default is what makes a Listener
 * setup look dead — the PC dials *out* instead of binding the port, so the sender at the other end finds
 * nobody to connect to and the source stays black.
 *
 * A Listener also has to bind a wildcard address rather than the PC's own LAN IP. The LAN IP is what the
 * *sender* dials (`SrtSetupInfo.recommended_url`); pointing the receiving end at it would have the PC
 * call itself.
 */
import { type SrtMode } from '../protocol/types'

/** Port an SRT listener binds when the operator supplies no URL (§4.4). */
export const SRT_LISTEN_PORT = 9000

/**
 * Normalizes `url` for `mode`. An explicit `mode=` already in the query is the operator's own choice and
 * is preserved, as is any other query parameter (`passphrase`, `streamid`, ...). A Listener keeps only
 * the port from `url`; an empty `url` binds `SRT_LISTEN_PORT`.
 */
export function srtUrlForMode(url: string | null | undefined, mode: SrtMode): string {
  let text = (url ?? '').trim()
  if (text.length > 0 && !text.includes('://')) {
    text = `srt://${text}` // "192.168.1.50:9000" typed bare
  }

  const mark = text.indexOf('?')
  let authority = mark >= 0 ? text.slice(0, mark) : text
  let query = mark >= 0 ? text.slice(mark + 1) : ''

  const listener = mode === 'listener'
  if (listener) {
    // 0.0.0.0 accepts the push on every NIC, so the sender may use any of the host candidates.
    authority = `srt://0.0.0.0:${parsePort(authority)}`
  }

  const hasMode = query.split('&').some((part) => part.toLowerCase().startsWith('mode='))
  if (!hasMode) {
    const modeParam = `mode=${mode}`
    query = query.length === 0 ? modeParam : `${query}&${modeParam}`
  }

  return `${authority}?${query}`
}

/** The mode `url` declares, defaulting to `caller` the same way libsrt does. */
export function srtModeOf(url: string | null | undefined): SrtMode {
  const query = (url ?? '').split('?')[1] ?? ''
  const declared = query.split('&').find((part) => part.toLowerCase().startsWith('mode='))
  return declared?.slice('mode='.length).toLowerCase() === 'listener' ? 'listener' : 'caller'
}

function parsePort(authority: string): number {
  const match = /:(\d+)\s*$/.exec(authority)
  const port = match ? Number(match[1]) : NaN
  return Number.isInteger(port) && port > 0 ? port : SRT_LISTEN_PORT
}
