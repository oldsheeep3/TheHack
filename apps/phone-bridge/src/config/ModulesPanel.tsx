import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { MAX_MODULES, VR_TARGETS, type ModuleMapping, type SourceInfo } from '../protocol/types'
import { Field, Section, dangerButtonClass, primaryButtonClass, selectClass } from './ui'

function emptyModule(index: number): ModuleMapping {
  return {
    index,
    src1: { source_id: null, vr_target: 'transition' },
    src2: { source_id: null, vr_target: 'opacity' },
  }
}

interface ModulesPanelProps {
  apiClient: ApiClient
  sources: SourceInfo[]
  onError: (message: string) => void
}

export function ModulesPanel({ apiClient, sources, onError }: ModulesPanelProps): JSX.Element {
  const [modules, setModules] = useState<ModuleMapping[]>([])
  const [loaded, setLoaded] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiClient
      .getModules(controller.signal)
      .then((config) => {
        setModules(config.modules)
        setLoaded(true)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        onError(error instanceof Error ? error.message : String(error))
      })
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient])

  const addModule = (): void => {
    if (modules.length >= MAX_MODULES) return
    setModules((prev) => [...prev, emptyModule(prev.length)])
  }

  const removeModule = (index: number): void => {
    setModules((prev) => prev.filter((mod) => mod.index !== index).map((mod, i) => ({ ...mod, index: i })))
  }

  const updateModule = (index: number, key: 'src1' | 'src2', patch: Partial<ModuleMapping['src1']>): void => {
    setModules((prev) => prev.map((mod) => (mod.index === index ? { ...mod, [key]: { ...mod[key], ...patch } } : mod)))
  }

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setModules({ modules })
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Section
      title={`モジュール割付 (${modules.length}/${MAX_MODULES})`}
      hint="物理モジュールの src1/src2 をソースに紐付け、VR（つまみ）の割当先を決めます。"
      actions={
        <button
          type="button"
          disabled={!loaded || modules.length >= MAX_MODULES}
          onClick={addModule}
          className={primaryButtonClass}
        >
          + モジュール追加
        </button>
      }
    >
      {!loaded ? (
        <p className="text-sm text-text-muted">読み込み中…</p>
      ) : (
        <div className="flex flex-col gap-3">
          {modules.length === 0 && <p className="text-sm text-text-muted">モジュールが登録されていません。</p>}

          {modules.map((module) => (
            <div key={module.index} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-text-primary">モジュール {module.index}</span>
                <button type="button" onClick={() => removeModule(module.index)} className={dangerButtonClass}>
                  削除
                </button>
              </div>

              {(['src1', 'src2'] as const).map((key) => (
                <div key={key} className="flex flex-col gap-1 rounded border border-border p-2">
                  <span className="text-xs text-text-muted">{key}</span>
                  <Field label="論理ソース">
                    <select
                      value={module[key].source_id ?? ''}
                      onChange={(event) => updateModule(module.index, key, { source_id: event.target.value || null })}
                      className={selectClass}
                    >
                      <option value="">未割当</option>
                      {sources
                        .filter((source) => source.id !== null)
                        .map((source) => (
                          <option key={source.id} value={source.id as string}>
                            {source.name}
                          </option>
                        ))}
                    </select>
                  </Field>
                  <Field label="VR割当先">
                    <select
                      value={VR_TARGETS.includes(module[key].vr_target) ? module[key].vr_target : ''}
                      onChange={(event) => updateModule(module.index, key, { vr_target: event.target.value })}
                      className={selectClass}
                    >
                      {!VR_TARGETS.includes(module[key].vr_target) && (
                        <option value="">{module[key].vr_target || '（未設定）'}</option>
                      )}
                      {VR_TARGETS.map((target) => (
                        <option key={target} value={target}>
                          {target}
                        </option>
                      ))}
                    </select>
                  </Field>
                </div>
              ))}
            </div>
          ))}

          <button
            type="button"
            disabled={busy}
            onClick={() => void handleApply()}
            className={`self-start ${primaryButtonClass}`}
          >
            モジュール割付を適用
          </button>
        </div>
      )}
    </Section>
  )
}
