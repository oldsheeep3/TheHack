/**
 * Manual host/port entry for the PC connection (§2.1 of
 * docs/specs/phone-web-bridge.md). mDNS hostnames aren't resolvable directly
 * from a browser, so manual entry + persistence is the first-class path;
 * an mDNS hostname can simply be typed into the same field.
 */

const STORAGE_KEY = 'phone-bridge.pc-host'

export const DEFAULT_PORT = 8080

export interface HostConfig {
  host: string
  port: number
}

export function loadHostConfig(): HostConfig | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    return isHostConfig(parsed) ? parsed : null
  } catch {
    return null
  }
}

export function saveHostConfig(config: HostConfig): void {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(config))
}

function isHostConfig(value: unknown): value is HostConfig {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  return (
    typeof candidate.host === 'string' &&
    candidate.host.trim() !== '' &&
    typeof candidate.port === 'number' &&
    Number.isFinite(candidate.port)
  )
}
