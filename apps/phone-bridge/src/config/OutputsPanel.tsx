import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import {
  DEFAULT_HDMI_DISPLAY_ID,
  MAX_OUTPUTS,
  OUTPUT_KINDS,
  OUTPUT_KIND_LABEL,
  PGM_BUSES,
  defaultNdiSenderName,
  isHdmiOutput,
  isNdiOutput,
  maxOutputsOf,
  outputKindOf,
  outputSinksOf,
  type NdiOutputSink,
  type OutputAssignment,
  type OutputKind,
  type OutputSink,
  type PgmBus,
} from '../protocol/types'
import { Field, Section, dangerButtonClass, primaryButtonClass, secondaryButtonClass, selectClass } from './ui'

interface OutputsPanelProps {
  apiClient: ApiClient
  onError: (message: string) => void
}

/**
 * The operator-built output table (§4.2). Sinks are added and removed here, at most
 * `maxOutputsOf(kind)` of each kind and `MAX_OUTPUTS` in total — the same ceilings the PC enforces, so
 * the add buttons disable rather than letting the API bounce the whole table.
 */
export function OutputsPanel({ apiClient, onError }: OutputsPanelProps): JSX.Element {
  const [outputs, setOutputs] = useState<OutputAssignment[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiClient
      .getOutputs(controller.signal)
      .then((config) => {
        setOutputs(config.outputs)
        setLoaded(true)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        onError(error instanceof Error ? error.message : String(error))
      })
    return () => controller.abort()
    // Loaded once rather than polled: this table is what the operator is editing, and a poll would
    // overwrite half-finished edits. onError is intentionally not a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient])

  const updateOutput = (index: number, patch: Partial<OutputAssignment>): void => {
    setOutputs((prev) =>
      prev.map((output, i) => (i === index ? ({ ...output, ...patch } as OutputAssignment) : output)),
    )
  }

  /** The lowest-ordinal sink of `kind` not already in the table, or null when it cannot take another. */
  const nextAvailable = (kind: OutputKind): OutputSink | null => {
    if (outputs.length >= MAX_OUTPUTS) return null
    const used = new Set(outputs.map((output) => output.sink))
    const ofKind = outputs.filter((output) => outputKindOf(output.sink) === kind)
    if (ofKind.length >= maxOutputsOf(kind)) return null
    return outputSinksOf(kind).find((sink) => !used.has(sink)) ?? null
  }

  const addOutput = (kind: OutputKind): void => {
    const sink = nextAvailable(kind)
    if (!sink) return

    // A new sink starts on whichever bus has fewer sinks, so adding one tends to fill the gap the
    // "every program bus needs an output" rule cares about rather than doubling up on PGM1.
    const pgm1Count = outputs.filter((output) => output.source === 'PGM1').length
    const pgm2Count = outputs.filter((output) => output.source === 'PGM2').length
    const source: PgmBus = pgm1Count <= pgm2Count ? 'PGM1' : 'PGM2'

    const added: OutputAssignment =
      kind === 'HDMI'
        ? {
            sink: sink as Extract<OutputSink, `HDMI${string}`>,
            source,
            display_id: DEFAULT_HDMI_DISPLAY_ID,
            hide_cursor: true,
            fullscreen: true,
          }
        : kind === 'NDI'
          ? { sink: sink as NdiOutputSink, source, ndi_name: defaultNdiSenderName(sink as NdiOutputSink) }
          : { sink: sink as Extract<OutputSink, `VCAM${string}`>, source }

    setOutputs((prev) => [...prev, added])
  }

  const removeOutput = (index: number): void => {
    setOutputs((prev) => prev.filter((_, i) => i !== index))
  }

  const missingBuses = PGM_BUSES.filter((bus) => !outputs.some((output) => output.source === bus))

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
    <Section
      title={`出力割当 (${outputs.length}/${MAX_OUTPUTS})`}
      hint="Webcam 1本・HDMI 3本・NDI 3本・合計6本まで。各プログラムバスに最低1つ必要です。"
    >
      {!loaded ? (
        <p className="text-sm text-text-muted">読み込み中…</p>
      ) : (
        <div className="flex flex-col gap-3">
          {outputs.length === 0 && <p className="text-sm text-text-muted">出力がありません。</p>}

          {outputs.map((output, index) => (
            <div key={output.sink} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-text-primary">
                  {output.sink}
                  <span className="ml-2 text-xs text-text-muted">{OUTPUT_KIND_LABEL[outputKindOf(output.sink)]}</span>
                </span>
                <button type="button" onClick={() => removeOutput(index)} className={dangerButtonClass}>
                  削除
                </button>
              </div>

              <Field label="割当ソース">
                <select
                  value={output.source}
                  onChange={(event) => updateOutput(index, { source: event.target.value as PgmBus })}
                  className={selectClass}
                >
                  {PGM_BUSES.map((bus) => (
                    <option key={bus} value={bus}>
                      {bus}
                    </option>
                  ))}
                </select>
              </Field>

              {isHdmiOutput(output) && (
                <>
                  <Field label="ディスプレイID">
                    <input
                      type="number"
                      value={output.display_id}
                      onChange={(event) => updateOutput(index, { display_id: Number(event.target.value) })}
                      className={`${selectClass} w-24`}
                    />
                  </Field>
                  <Field label="カーソル非表示">
                    <input
                      type="checkbox"
                      checked={output.hide_cursor}
                      onChange={(event) => updateOutput(index, { hide_cursor: event.target.checked })}
                      className="h-5 w-5 accent-accent"
                    />
                  </Field>
                  <Field label="全画面">
                    <input
                      type="checkbox"
                      checked={output.fullscreen}
                      onChange={(event) => updateOutput(index, { fullscreen: event.target.checked })}
                      className="h-5 w-5 accent-accent"
                    />
                  </Field>
                  <p className="text-xs text-text-muted">
                    実際に映すにはPC側で「Open projectors」を実行してください（割当だけではウィンドウは開きません）。
                  </p>
                </>
              )}

              {isNdiOutput(output) && (
                <Field label="NDI送信名">
                  <input
                    type="text"
                    value={output.ndi_name ?? ''}
                    placeholder={defaultNdiSenderName(output.sink)}
                    onChange={(event) => updateOutput(index, { ndi_name: event.target.value || null })}
                    className={`${selectClass} w-56`}
                  />
                </Field>
              )}
            </div>
          ))}

          <div className="flex flex-wrap gap-2">
            {OUTPUT_KINDS.map((kind) => (
              <button
                key={kind}
                type="button"
                disabled={nextAvailable(kind) === null}
                onClick={() => addOutput(kind)}
                className={secondaryButtonClass}
              >
                + {OUTPUT_KIND_LABEL[kind]}
              </button>
            ))}
          </div>

          {missingBuses.length > 0 && (
            <p className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              {missingBuses.join(' / ')} に出力がありません。各プログラムバスに最低1つ割り当ててください。
            </p>
          )}

          <button
            type="button"
            disabled={busy || missingBuses.length > 0}
            onClick={() => void handleApply()}
            className={`self-start ${primaryButtonClass}`}
          >
            出力設定を適用
          </button>
        </div>
      )}
    </Section>
  )
}
