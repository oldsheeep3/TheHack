import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import {
  ATEM_ACTIONS,
  HID_RELAY_CONTROLLER_ID,
  MAX_MODULES,
  MODULE_SWITCH_IDS,
  MODULE_SWITCH_LABEL,
  atemActionTakesSource,
  type AtemAction,
  type AtemButtonMapping,
  type AtemConfig,
  type AtemDeviceInfo,
  type ModuleSwitchId,
} from '../protocol/types'
import {
  Field,
  Section,
  StackedField,
  inputClass,
  primaryButtonClass,
  secondaryButtonClass,
  selectClass,
} from './ui'

/** M/E labels; the wire value is the 0-based index of the entry (same as the PC's settings window). */
const MIX_EFFECTS = ['M/E 1', 'M/E 2']

const NO_ACTION = ''

interface AtemPanelProps {
  apiClient: ApiClient
  onError: (message: string) => void
}

/**
 * ATEM remote control (§4.2): which switcher to drive, and which module switch relays which ATEM
 * command. The PC keys relayed mappings by `controller_id` + module + switch, so rows written here are
 * the same rows its own ATEM settings window writes.
 */
export function AtemPanel({ apiClient, onError }: AtemPanelProps): JSX.Element {
  const [config, setConfig] = useState<AtemConfig>({ enabled: false, ip: '', mappings: [], name: null })
  const [devices, setDevices] = useState<AtemDeviceInfo[]>([])
  const [scanning, setScanning] = useState(false)
  const [scanStatus, setScanStatus] = useState<string | null>(null)
  const [moduleCount, setModuleCount] = useState(1)
  const [streamUrl, setStreamUrl] = useState('')
  const [streamStatus, setStreamStatus] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiClient
      .getAtemConfig(controller.signal)
      .then((result) => {
        setConfig({ ...result, mappings: result.mappings ?? [] })
        const highest = (result.mappings ?? []).reduce((max, mapping) => Math.max(max, mapping.module_index), 0)
        setModuleCount(Math.min(MAX_MODULES, highest + 1))
        setLoaded(true)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        onError(error instanceof Error ? error.message : String(error))
      })
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient])

  const mappingFor = (moduleIndex: number, switchId: ModuleSwitchId): AtemButtonMapping | undefined =>
    config.mappings.find((mapping) => mapping.module_index === moduleIndex && mapping.switch === switchId)

  const setMapping = (moduleIndex: number, switchId: ModuleSwitchId, patch: Partial<AtemButtonMapping> | null): void => {
    setConfig((prev) => {
      const rest = prev.mappings.filter(
        (mapping) => !(mapping.module_index === moduleIndex && mapping.switch === switchId),
      )
      if (patch === null) return { ...prev, mappings: rest }

      const existing = mappingFor(moduleIndex, switchId)
      const merged: AtemButtonMapping = {
        controller_id: HID_RELAY_CONTROLLER_ID,
        module_index: moduleIndex,
        switch: switchId,
        action: existing?.action ?? 'ProgramInput',
        mix_effect: existing?.mix_effect ?? 0,
        source: existing?.source ?? 1,
        ...patch,
      }
      return { ...prev, mappings: [...rest, merged] }
    })
  }

  const handleScan = async (): Promise<void> => {
    setScanning(true)
    setScanStatus('検索中…（LAN全体を走査するため時間がかかります）')
    try {
      const found = await apiClient.discoverAtemDevices()
      setDevices(found)
      setScanStatus(found.length === 0 ? 'ATEMが見つかりませんでした。' : `${found.length}台見つかりました。`)
    } catch (error) {
      setScanStatus(null)
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setScanning(false)
    }
  }

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setAtemConfig(config)
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  const handleConfigureStreaming = async (): Promise<void> => {
    setBusy(true)
    setStreamStatus(null)
    try {
      const applied = await apiClient.configureAtemStreaming({ url: streamUrl.trim(), start: true })
      setStreamStatus(
        applied ? 'ATEMのストリーミング出力を設定しました。' : 'ATEMが接続されていません（設定を適用して接続してください）。',
      )
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section
      title="ATEM 遠隔制御"
      hint="制御対象のATEMと、モジュールSW→ATEMコマンドの割付。ATEMのPGMは別途SRT/NDIソースとして受けます。"
    >
      {!loaded ? (
        <p className="text-sm text-text-muted">読み込み中…</p>
      ) : (
        <div className="flex flex-col gap-3">
          <Field label="このATEMに接続する">
            <input
              type="checkbox"
              checked={config.enabled}
              onChange={(event) => setConfig({ ...config, enabled: event.target.checked })}
              className="h-6 w-6 accent-accent"
            />
          </Field>

          <StackedField label="IPアドレス">
            <input
              type="text"
              value={config.ip}
              onChange={(event) => setConfig({ ...config, ip: event.target.value })}
              placeholder="192.168.10.240"
              className={inputClass}
            />
          </StackedField>

          {config.name && <p className="text-xs text-text-muted">選択中の機種: {config.name}</p>}

          <div className="flex flex-wrap items-center gap-2">
            <button type="button" disabled={scanning} onClick={() => void handleScan()} className={secondaryButtonClass}>
              ネットワークを検索
            </button>
            {scanStatus && <span className="text-xs text-text-muted">{scanStatus}</span>}
          </div>

          {devices.length > 0 && (
            <ul className="flex flex-col gap-1">
              {devices.map((device) => (
                <li key={device.ip}>
                  <button
                    type="button"
                    onClick={() => setConfig({ ...config, ip: device.ip, name: device.name })}
                    className="w-full rounded-md bg-surface-2 px-3 py-2 text-left text-sm text-text-primary"
                  >
                    {device.name} — {device.ip}
                  </button>
                </li>
              ))}
            </ul>
          )}

          <div className="rounded-md border border-border p-3">
            <div className="mb-2 flex items-center justify-between gap-2">
              <p className="text-sm font-medium text-text-primary">スイッチ割付</p>
              <Field label="モジュール数">
                <select
                  value={moduleCount}
                  onChange={(event) => setModuleCount(Number(event.target.value))}
                  className={selectClass}
                >
                  {Array.from({ length: MAX_MODULES }, (_, i) => i + 1).map((count) => (
                    <option key={count} value={count}>
                      {count}
                    </option>
                  ))}
                </select>
              </Field>
            </div>

            <div className="flex flex-col gap-3">
              {Array.from({ length: moduleCount }, (_, moduleIndex) => (
                <div key={moduleIndex} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
                  <span className="text-sm font-medium text-text-primary">モジュール {moduleIndex}</span>
                  {MODULE_SWITCH_IDS.map((switchId) => {
                    const mapping = mappingFor(moduleIndex, switchId)
                    const action = (mapping?.action as AtemAction | undefined) ?? NO_ACTION
                    return (
                      <div key={switchId} className="flex flex-wrap items-center gap-2">
                        <span className="w-24 text-xs text-text-muted">{MODULE_SWITCH_LABEL[switchId]}</span>
                        <select
                          value={action}
                          onChange={(event) =>
                            setMapping(
                              moduleIndex,
                              switchId,
                              event.target.value === NO_ACTION ? null : { action: event.target.value as AtemAction },
                            )
                          }
                          className={selectClass}
                        >
                          <option value={NO_ACTION}>（通常動作）</option>
                          {ATEM_ACTIONS.map((option) => (
                            <option key={option} value={option}>
                              {option}
                            </option>
                          ))}
                        </select>

                        {mapping && (
                          <>
                            <select
                              value={mapping.mix_effect}
                              onChange={(event) =>
                                setMapping(moduleIndex, switchId, { mix_effect: Number(event.target.value) })
                              }
                              className={selectClass}
                            >
                              {MIX_EFFECTS.map((label, index) => (
                                <option key={label} value={index}>
                                  {label}
                                </option>
                              ))}
                            </select>
                            {atemActionTakesSource(mapping.action as AtemAction) && (
                              <input
                                type="number"
                                value={mapping.source}
                                aria-label="ATEM入力番号"
                                onChange={(event) =>
                                  setMapping(moduleIndex, switchId, { source: Number(event.target.value) })
                                }
                                className={`${selectClass} w-20`}
                              />
                            )}
                          </>
                        )}
                      </div>
                    )
                  })}
                </div>
              ))}
            </div>
            <p className="mt-2 text-xs text-text-muted">
              「通常動作」のままのスイッチは、割り当てたソースをバスに載せる本来の動作を続けます。Cut/Auto はM/E全体に効くため入力番号は使いません。
            </p>
          </div>

          <button type="button" disabled={busy} onClick={() => void handleApply()} className={`self-start ${primaryButtonClass}`}>
            ATEM設定を適用
          </button>

          <div className="rounded-md border border-border p-3">
            <p className="mb-2 text-sm font-medium text-text-primary">ATEMのストリーミング出力</p>
            <p className="mb-2 text-xs text-text-muted">
              ATEMのPGMをこのPCへSRTで送り込む設定です。先に「入力ソース」でSRT（Listener）を作り、その受信URLをここに入れてください。
            </p>
            <StackedField label="送信先URL">
              <input
                type="text"
                value={streamUrl}
                onChange={(event) => setStreamUrl(event.target.value)}
                placeholder="srt://192.168.1.50:9000"
                className={inputClass}
              />
            </StackedField>
            <button
              type="button"
              disabled={busy || !streamUrl.trim()}
              onClick={() => void handleConfigureStreaming()}
              className={`mt-2 ${secondaryButtonClass}`}
            >
              ATEMのストリーミング出力を設定
            </button>
            {streamStatus && <p className="mt-2 text-xs text-text-muted">{streamStatus}</p>}
          </div>
        </div>
      )}
    </Section>
  )
}
