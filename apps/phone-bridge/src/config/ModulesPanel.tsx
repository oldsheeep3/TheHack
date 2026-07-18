import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { MAX_MODULES, type ModuleMapping, type SourceInfo } from '../protocol/types'
import { Section } from './ui'

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
  const [modules, setModules] = useState<ModuleMapping[]>([emptyModule(0)])
  const [busy, setBusy] = useState(false)

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
      actions={
        <button
          type="button"
          disabled={modules.length >= MAX_MODULES}
          onClick={addModule}
          className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          + モジュール追加
        </button>
      }
    >
      <div className="flex flex-col gap-3">
        {modules.map((module) => (
          <div key={module.index} className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium text-text-primary">モジュール {module.index}</span>
              <button
                type="button"
                onClick={() => removeModule(module.index)}
                className="rounded-md bg-danger/20 px-2 py-1 text-xs font-medium text-danger"
              >
                削除
              </button>
            </div>

            {(['src1', 'src2'] as const).map((key) => (
              <div key={key} className="flex flex-col gap-1 rounded border border-border p-2">
                <span className="text-xs text-text-muted">{key}</span>
                <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
                  論理ソース
                  <select
                    value={module[key].source_id ?? ''}
                    onChange={(event) =>
                      updateModule(module.index, key, { source_id: event.target.value || null })
                    }
                    className="rounded-md border border-border bg-surface-1 px-2 py-1 text-sm"
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
                </label>
                <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
                  VR割当先
                  <input
                    type="text"
                    value={module[key].vr_target}
                    onChange={(event) => updateModule(module.index, key, { vr_target: event.target.value })}
                    className="w-32 rounded-md border border-border bg-surface-1 px-2 py-1 text-sm"
                  />
                </label>
              </div>
            ))}
          </div>
        ))}

        <button
          type="button"
          disabled={busy}
          onClick={() => void handleApply()}
          className="self-start rounded-md bg-accent px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          モジュール割付を適用
        </button>
      </div>
    </Section>
  )
}
