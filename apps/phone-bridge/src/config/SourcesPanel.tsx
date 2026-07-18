import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { SOURCE_TYPES, type SourceInfo, type SourceStatus, type SourceType, type TallyState } from '../protocol/types'
import {
  buildSourceDefinition,
  emptySourceFormValues,
  validateSourceFormValues,
  type SourceFormValues,
} from './sourceForm'
import { Badge, Section } from './ui'

const STATUS_LABEL: Record<SourceStatus, string> = {
  Connected: '接続済み',
  Disconnected: '未接続',
  Error: 'エラー',
}

const STATUS_COLOR: Record<SourceStatus, string> = {
  Connected: 'bg-success',
  Disconnected: 'bg-text-muted',
  Error: 'bg-danger',
}

interface SourcesPanelProps {
  apiClient: ApiClient
  sources: SourceInfo[]
  tally: TallyState | null
  onReorder: (id: string, direction: 'up' | 'down') => void
  onChanged: () => void
  onError: (message: string) => void
}

export function SourcesPanel({ apiClient, sources, tally, onReorder, onChanged, onError }: SourcesPanelProps): JSX.Element {
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<SourceFormValues | null>(null)
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const startAdd = (): void => {
    setEditingId(null)
    setForm(emptySourceFormValues())
    setFormErrors([])
  }

  const startEdit = (source: SourceInfo): void => {
    if (!source.id) return
    setEditingId(source.id)
    setForm({ ...emptySourceFormValues(source.type ?? 'NDI'), id: source.id, name: source.name })
    setFormErrors([])
  }

  const cancelForm = (): void => {
    setEditingId(null)
    setForm(null)
    setFormErrors([])
  }

  const handleSubmit = async (): Promise<void> => {
    if (!form) return
    const errors = validateSourceFormValues(form)
    setFormErrors(errors)
    if (errors.length > 0) return
    const definition = buildSourceDefinition(form)
    setBusy(true)
    try {
      if (editingId) {
        await apiClient.updateSource(editingId, definition)
      } else {
        await apiClient.addSource(definition)
      }
      cancelForm()
      onChanged()
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  const handleDelete = async (id: string): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.deleteSource(id)
      onChanged()
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section
      title="入力ソース"
      actions={
        <button
          type="button"
          onClick={startAdd}
          className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white"
        >
          + ソース追加
        </button>
      }
    >
      {sources.length === 0 ? (
        <p className="text-sm text-text-muted">ソースがありません。</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {sources.map((source) => {
            const isPgm1 = tally?.active_pgm1.includes(source.channel) ?? false
            const isPgm2 = tally?.active_pgm2.includes(source.channel) ?? false
            const isPvw1 = tally?.active_pvw1.includes(source.channel) ?? false
            const isPvw2 = tally?.active_pvw2.includes(source.channel) ?? false
            return (
              <li
                key={source.channel}
                className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-surface-2 px-3 py-2"
              >
                <span className="flex items-center gap-2">
                  <span
                    className={`h-2.5 w-2.5 rounded-full ${STATUS_COLOR[source.status]}`}
                    aria-hidden="true"
                  />
                  <span className="font-medium text-text-primary">{source.name}</span>
                  <span className="text-xs text-text-muted">
                    {source.type ?? source.protocol} / {STATUS_LABEL[source.status]}
                  </span>
                </span>
                <span className="flex flex-wrap items-center gap-1">
                  {isPgm1 && <Badge label="PGM1" tone="danger" />}
                  {isPgm2 && <Badge label="PGM2" tone="danger" />}
                  {isPvw1 && <Badge label="PVW1" tone="success" />}
                  {isPvw2 && <Badge label="PVW2" tone="success" />}
                  {source.id && (
                    <>
                      <button
                        type="button"
                        onClick={() => onReorder(source.id as string, 'up')}
                        aria-label="上へ移動"
                        className="rounded-md bg-surface-1 px-2 py-2 text-sm text-text-primary"
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        onClick={() => onReorder(source.id as string, 'down')}
                        aria-label="下へ移動"
                        className="rounded-md bg-surface-1 px-2 py-2 text-sm text-text-primary"
                      >
                        ↓
                      </button>
                      <button
                        type="button"
                        onClick={() => startEdit(source)}
                        className="rounded-md bg-accent-muted px-3 py-2 text-sm font-medium text-text-primary"
                      >
                        編集
                      </button>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void handleDelete(source.id as string)}
                        className="rounded-md bg-danger/20 px-3 py-2 text-sm font-medium text-danger disabled:opacity-40"
                      >
                        削除
                      </button>
                    </>
                  )}
                </span>
              </li>
            )
          })}
        </ul>
      )}

      {form && (
        <div className="mt-4 flex flex-col gap-3 rounded-lg border border-border p-3">
          <p className="text-sm font-medium text-text-primary">
            {editingId ? `ソース編集: ${editingId}` : '新規ソース'}
          </p>

          {formErrors.length > 0 && (
            <ul className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              {formErrors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          )}

          <label className="flex flex-col gap-1 text-sm text-text-primary">
            ID
            <input
              type="text"
              value={form.id}
              disabled={editingId !== null}
              onChange={(event) => setForm({ ...form, id: event.target.value })}
              className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base disabled:opacity-60"
            />
          </label>

          <label className="flex flex-col gap-1 text-sm text-text-primary">
            名前
            <input
              type="text"
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
              className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
            />
          </label>

          <label className="flex flex-col gap-1 text-sm text-text-primary">
            種別
            <select
              value={form.type}
              onChange={(event) => setForm({ ...form, type: event.target.value as SourceType })}
              className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
            >
              {SOURCE_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </label>

          {form.type === 'NDI' && (
            <label className="flex flex-col gap-1 text-sm text-text-primary">
              NDIソース名
              <input
                type="text"
                value={form.ndiSourceName}
                onChange={(event) => setForm({ ...form, ndiSourceName: event.target.value })}
                className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
              />
            </label>
          )}

          {form.type === 'WEBCAM' && (
            <>
              <label className="flex flex-col gap-1 text-sm text-text-primary">
                デバイスID
                <input
                  type="text"
                  value={form.webcamDeviceId}
                  onChange={(event) => setForm({ ...form, webcamDeviceId: event.target.value })}
                  className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
                />
              </label>
              <label className="flex flex-col gap-1 text-sm text-text-primary">
                フォーマット（任意, 例: 1920x1080@30）
                <input
                  type="text"
                  value={form.webcamFormat}
                  onChange={(event) => setForm({ ...form, webcamFormat: event.target.value })}
                  className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
                />
              </label>
            </>
          )}

          {form.type === 'SRT' && (
            <>
              <label className="flex flex-col gap-1 text-sm text-text-primary">
                URL
                <input
                  type="text"
                  value={form.srtUrl}
                  onChange={(event) => setForm({ ...form, srtUrl: event.target.value })}
                  className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
                />
              </label>
              <label className="flex flex-col gap-1 text-sm text-text-primary">
                レイテンシ (ms)
                <input
                  type="number"
                  value={form.srtLatencyMs}
                  onChange={(event) => setForm({ ...form, srtLatencyMs: Number(event.target.value) })}
                  className="rounded-md border border-border bg-surface-2 px-3 py-2 text-base"
                />
              </label>
            </>
          )}

          <div className="flex gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={() => void handleSubmit()}
              className="rounded-md bg-accent px-4 py-2 text-base font-medium text-white disabled:opacity-40"
            >
              {editingId ? '更新' : '追加'}
            </button>
            <button
              type="button"
              onClick={cancelForm}
              className="rounded-md bg-surface-2 px-4 py-2 text-base font-medium text-text-primary"
            >
              キャンセル
            </button>
          </div>
        </div>
      )}
    </Section>
  )
}
