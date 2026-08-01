import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { PGM_BUSES, type AudioDeviceInfo, type AudioOutputAssignment, type PgmBus } from '../protocol/types'
import { Field, Section, dangerButtonClass, primaryButtonClass, secondaryButtonClass, selectClass } from './ui'

interface AudioPanelProps {
  apiClient: ApiClient
  onError: (message: string) => void
}

/**
 * Bus-to-device audio routing (`GET/PUT /api/v1/audio/outputs`). A bus may appear more than once to feed
 * several devices at the same time — front of house plus a recorder — which is why this is a table and
 * not a single "monitoring device" setting. An empty table is legal and means nothing is played out.
 */
export function AudioPanel({ apiClient, onError }: AudioPanelProps): JSX.Element {
  const [devices, setDevices] = useState<AudioDeviceInfo[]>([])
  const [outputs, setOutputs] = useState<AudioOutputAssignment[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)

  const load = (signal?: AbortSignal): void => {
    Promise.all([apiClient.getAudioDevices(signal), apiClient.getAudioOutputs(signal)])
      .then(([deviceList, config]) => {
        setDevices(deviceList)
        setOutputs(config.outputs)
        setLoaded(true)
      })
      .catch((error: unknown) => {
        if (signal?.aborted) return
        onError(error instanceof Error ? error.message : String(error))
      })
  }

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient])

  const addRow = (bus: PgmBus): void => {
    setOutputs((prev) => [...prev, { bus, device_id: '', device_name: null }])
  }

  const updateRow = (index: number, patch: Partial<AudioOutputAssignment>): void => {
    setOutputs((prev) => prev.map((row, i) => (i === index ? { ...row, ...patch } : row)))
  }

  const removeRow = (index: number): void => {
    setOutputs((prev) => prev.filter((_, i) => i !== index))
  }

  // Two rows on the same bus and device would double up the same audio, which the PC rejects.
  const duplicates = outputs.filter(
    (row, index) => outputs.findIndex((other) => other.bus === row.bus && other.device_id === row.device_id) !== index,
  )

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setAudioOutputs({ outputs })
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section
      title="音声出力"
      hint="プログラムバスごとに再生デバイスへ割り当てます（1つのバスを複数デバイスへ出せます）。"
      actions={
        <button type="button" onClick={() => load()} className={secondaryButtonClass}>
          再スキャン
        </button>
      }
    >
      {!loaded ? (
        <p className="text-sm text-text-muted">読み込み中…</p>
      ) : (
        <div className="flex flex-col gap-3">
          {outputs.length === 0 && (
            <p className="text-sm text-text-muted">割当がありません（音声は再生されません）。</p>
          )}

          {outputs.map((row, index) => (
            <div key={`${row.bus}-${row.device_id}-${index}`} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-text-primary">{row.bus}</span>
                <button type="button" onClick={() => removeRow(index)} className={dangerButtonClass}>
                  削除
                </button>
              </div>
              <Field label="出力先デバイス">
                <select
                  value={row.device_id}
                  onChange={(event) => {
                    const device = devices.find((candidate) => candidate.id === event.target.value)
                    updateRow(index, { device_id: event.target.value, device_name: device?.name ?? null })
                  }}
                  className={selectClass}
                >
                  <option value="">既定のデバイス</option>
                  {devices.map((device) => (
                    <option key={device.id} value={device.id}>
                      {device.name}
                      {device.is_default ? '（既定）' : ''}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
          ))}

          <div className="flex flex-wrap gap-2">
            {PGM_BUSES.map((bus) => (
              <button key={bus} type="button" onClick={() => addRow(bus)} className={secondaryButtonClass}>
                + {bus}
              </button>
            ))}
          </div>

          {devices.length === 0 && (
            <p className="text-xs text-text-muted">
              再生デバイスが見つかりません（PC側のエンジンが起動していない可能性があります）。
            </p>
          )}

          {duplicates.length > 0 && (
            <p className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              同じバスを同じデバイスへ2回割り当てています。重複を削除してください。
            </p>
          )}

          <button
            type="button"
            disabled={busy || duplicates.length > 0}
            onClick={() => void handleApply()}
            className={`self-start ${primaryButtonClass}`}
          >
            音声出力を適用
          </button>
        </div>
      )}
    </Section>
  )
}
