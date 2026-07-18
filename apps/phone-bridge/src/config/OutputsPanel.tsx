import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { PGM_BUSES, type OutputAssignment, type PgmBus } from '../protocol/types'
import { Section } from './ui'

const DEFAULT_OUTPUTS: OutputAssignment[] = [
  { sink: 'VCAM1', source: 'PGM1' },
  { sink: 'VCAM2', source: 'PGM2' },
  { sink: 'HDMI', source: 'PGM1', display_id: 1, hide_cursor: true, fullscreen: true },
]

interface OutputsPanelProps {
  apiClient: ApiClient
  onError: (message: string) => void
}

export function OutputsPanel({ apiClient, onError }: OutputsPanelProps): JSX.Element {
  const [outputs, setOutputs] = useState<OutputAssignment[]>(DEFAULT_OUTPUTS)
  const [busy, setBusy] = useState(false)

  const updateOutput = (index: number, patch: Partial<OutputAssignment>): void => {
    setOutputs((prev) => prev.map((output, i) => (i === index ? ({ ...output, ...patch } as OutputAssignment) : output)))
  }

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setOutputs({ outputs })
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section title="出力割当（仮想カメラ×2 / HDMI）">
      <div className="flex flex-col gap-3">
        {outputs.map((output, index) => (
          <div key={output.sink} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
            <span className="text-sm font-medium text-text-primary">{output.sink}</span>
            <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
              割当ソース
              <select
                value={output.source}
                onChange={(event) => updateOutput(index, { source: event.target.value as PgmBus })}
                className="rounded-md border border-border bg-surface-1 px-2 py-1 text-sm"
              >
                {PGM_BUSES.map((bus) => (
                  <option key={bus} value={bus}>
                    {bus}
                  </option>
                ))}
              </select>
            </label>

            {output.sink === 'HDMI' && (
              <>
                <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
                  ディスプレイID
                  <input
                    type="number"
                    value={output.display_id}
                    onChange={(event) => updateOutput(index, { display_id: Number(event.target.value) })}
                    className="w-24 rounded-md border border-border bg-surface-1 px-2 py-1 text-sm"
                  />
                </label>
                <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
                  カーソル非表示
                  <input
                    type="checkbox"
                    checked={output.hide_cursor}
                    onChange={(event) => updateOutput(index, { hide_cursor: event.target.checked })}
                    className="h-5 w-5 accent-accent"
                  />
                </label>
                <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
                  全画面
                  <input
                    type="checkbox"
                    checked={output.fullscreen}
                    onChange={(event) => updateOutput(index, { fullscreen: event.target.checked })}
                    className="h-5 w-5 accent-accent"
                  />
                </label>
              </>
            )}
          </div>
        ))}

        <button
          type="button"
          disabled={busy}
          onClick={() => void handleApply()}
          className="self-start rounded-md bg-accent px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          出力設定を適用
        </button>
      </div>
    </Section>
  )
}
