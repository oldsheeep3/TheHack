/**
 * Feature detection for the USB→network bridge (§2.1 of
 * docs/specs/phone-web-bridge.md). Web Serial / WebUSB require a secure
 * context (HTTPS or localhost) and a Chromium-based browser; later tasks
 * (W-002) use this to gate the relay UI and explain unsupported cases.
 */

export interface BrowserCapabilities {
  isSecureContext: boolean
  hasWebSerial: boolean
  hasWebUsb: boolean
  /** True when at least one bridge transport (Serial or USB) is usable. */
  canBridgeUsb: boolean
}

export function getBrowserCapabilities(): BrowserCapabilities {
  const isSecureContext = window.isSecureContext
  const hasWebSerial = isSecureContext && 'serial' in navigator
  const hasWebUsb = isSecureContext && 'usb' in navigator

  return {
    isSecureContext,
    hasWebSerial,
    hasWebUsb,
    canBridgeUsb: hasWebSerial || hasWebUsb,
  }
}
