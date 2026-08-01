import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import {
  SOURCE_AUDIO_MODE_LABEL,
  SOURCE_TYPE_LABEL,
  type SourceDefinition,
  type SourceInfo,
  type SourceStatus,
} from '../protocol/types'
import { SourceEditor } from './SourceEditor'
import {
  buildSourceDefinition,
  emptySourceFormValues,
  sourceFormValuesFromDefinition,
  validateSourceFormValues,
  type SourceFormValues,
} from './sourceForm'
import { Badge, Section, dangerButtonClass, primaryButtonClass, secondaryButtonClass } from './ui'

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
  /** Per-type configuration from `GET /api/v1/sources/definitions`, keyed by source id. */
  definitions: Map<string, SourceDefinition>
  onReorder: (id: string, direction: 'up' | 'down') => void
  onChanged: () => void
  onError: (message: string) => void
}

export function SourcesPanel({
  apiClient,
  sources,
  definitions,
  onReorder,
  onChanged,
  onError,
}: SourcesPanelProps): JSX.Element {
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
    const definition = definitions.get(source.id)
    if (!definition) {
      // Without the definition a "save" would replace the source with whatever blanks the form held,
      // so the edit is refused rather than offered as a trap.
      onError(`${source.name} の設定をPCから取得できていません。少し待ってから再度お試しください。`)
      return
    }
    setEditingId(source.id)
    setForm(sourceFormValuesFromDefinition(definition))
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
      hint="NDI / ウェブカメラ / SRT / 静止画 / Webページ / ミックス を追加・編集します。"
      actions={
        <button type="button" onClick={startAdd} className={primaryButtonClass}>
          + ソース追加
        </button>
      }
    >
      {sources.length === 0 ? (
        <p className="text-sm text-text-muted">ソースがありません。</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {sources.map((source) => {
            const definition = source.id ? definitions.get(source.id) : undefined
            return (
              <li
                key={source.channel}
                className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-surface-2 px-3 py-2"
              >
                <span className="flex items-center gap-2">
                  <span className={`h-2.5 w-2.5 rounded-full ${STATUS_COLOR[source.status]}`} aria-hidden="true" />
                  <span className="font-medium text-text-primary">{source.name}</span>
                  <span className="text-xs text-text-muted">
                    {definition ? SOURCE_TYPE_LABEL[definition.type] : source.protocol} /{' '}
                    {STATUS_LABEL[source.status]}
                    {source.resolution ? ` / ${source.resolution}` : ''}
                  </span>
                </span>
                <span className="flex flex-wrap items-center gap-1">
                  {definition?.audio_mode && definition.audio_mode !== 'AFV' && (
                    <Badge label={SOURCE_AUDIO_MODE_LABEL[definition.audio_mode]} tone="muted" />
                  )}
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
                      <button type="button" onClick={() => startEdit(source)} className={secondaryButtonClass}>
                        編集
                      </button>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void handleDelete(source.id as string)}
                        className={dangerButtonClass}
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
        <SourceEditor
          apiClient={apiClient}
          sources={sources}
          values={form}
          onChange={setForm}
          editingId={editingId}
          errors={formErrors}
          busy={busy}
          onSubmit={() => void handleSubmit()}
          onCancel={cancelForm}
        />
      )}
    </Section>
  )
}
