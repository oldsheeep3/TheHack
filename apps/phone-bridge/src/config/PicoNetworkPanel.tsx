import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import type { PicoNetworkConfig } from '../protocol/types'
import { Section } from './ui'

const EMPTY_CONFIG: PicoNetworkConfig = {
  wifi_ssid: '',
  wifi_password: '',
  controller_id: '',
  bluetooth_enabled: false,
}

interface PicoNetworkPanelProps {
  apiClient: ApiClient
  onError: (message: string) => void
}

export function PicoNetworkPanel({ apiClient, onError }: PicoNetworkPanelProps): JSX.Element {
  const [config, setConfig] = useState<PicoNetworkConfig>(EMPTY_CONFIG)
  const [busy, setBusy] = useState(false)

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setPicoNetwork({
        wifi_ssid: config.wifi_ssid?.trim() || null,
        wifi_password: config.wifi_password?.trim() || null,
        controller_id: config.controller_id?.trim() || null,
        bluetooth_enabled: config.bluetooth_enabled,
      })
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section title="Pico ネットワーク設定">
      <div className="flex flex-col gap-3">
        <label className="flex flex-col gap-1 text-sm text-text-primary">
          コントローラーID
          <input
            type="text"
            value={config.controller_id ?? ''}
            onChange={(event) => setConfig({ ...config, controller_id: event.target.value })}
            placeholder="main / sub"
            className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
          />
        </label>
        <label className="flex flex-col gap-1 text-sm text-text-primary">
          Wi-Fi SSID
          <input
            type="text"
            value={config.wifi_ssid ?? ''}
            onChange={(event) => setConfig({ ...config, wifi_ssid: event.target.value })}
            className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
          />
        </label>
        <label className="flex flex-col gap-1 text-sm text-text-primary">
          Wi-Fi パスワード
          <input
            type="password"
            value={config.wifi_password ?? ''}
            onChange={(event) => setConfig({ ...config, wifi_password: event.target.value })}
            className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
          />
        </label>
        <label className="flex items-center justify-between gap-2 text-base text-text-primary">
          Bluetooth 有効化
          <input
            type="checkbox"
            checked={config.bluetooth_enabled}
            onChange={(event) => setConfig({ ...config, bluetooth_enabled: event.target.checked })}
            className="h-6 w-6 accent-accent"
          />
        </label>

        <button
          type="button"
          disabled={busy}
          onClick={() => void handleApply()}
          className="self-start rounded-md bg-accent px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          Pico設定を送信
        </button>
      </div>
    </Section>
  )
}
