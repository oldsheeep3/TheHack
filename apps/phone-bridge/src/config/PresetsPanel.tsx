import { useState } from 'react'
import type { JSX } from 'react'
import type { MultiviewGrid, MultiviewRegion, PgmBus, ProgramLayer } from '../protocol/types'
import type { PresetStore, ScenePreset } from './presets'
import { Section } from './ui'

interface PresetsPanelProps {
  presetStore: PresetStore
  programs: Record<PgmBus, ProgramLayer[]>
  multiviewGrid: MultiviewGrid
  multiviewRegions: MultiviewRegion[]
  onLoad: (preset: ScenePreset) => void
}

export function PresetsPanel({
  presetStore,
  programs,
  multiviewGrid,
  multiviewRegions,
  onLoad,
}: PresetsPanelProps): JSX.Element {
  const [presetNames, setPresetNames] = useState<string[]>(() => presetStore.list())
  const [newPresetName, setNewPresetName] = useState('')

  const handleSave = (): void => {
    const name = newPresetName.trim()
    if (!name) return
    presetStore.save({ name, programs, multiviewGrid, multiviewRegions })
    setPresetNames(presetStore.list())
    setNewPresetName('')
  }

  const handleLoad = (name: string): void => {
    const preset = presetStore.load(name)
    if (preset) onLoad(preset)
  }

  const handleRemove = (name: string): void => {
    presetStore.remove(name)
    setPresetNames(presetStore.list())
  }

  return (
    <Section title="シーンプリセット">
      <div className="flex gap-2">
        <input
          type="text"
          value={newPresetName}
          onChange={(event) => setNewPresetName(event.target.value)}
          placeholder="プリセット名"
          className="flex-1 rounded-md border border-border bg-surface-2 px-3 py-2 text-base text-text-primary"
        />
        <button
          type="button"
          onClick={handleSave}
          disabled={!newPresetName.trim()}
          className="rounded-md bg-accent px-4 py-2 text-base font-medium text-white disabled:cursor-not-allowed disabled:opacity-40"
        >
          保存
        </button>
      </div>

      {presetNames.length === 0 ? (
        <p className="mt-3 text-sm text-text-muted">保存済みプリセットはありません。</p>
      ) : (
        <ul className="mt-3 flex flex-col gap-2">
          {presetNames.map((name) => (
            <li key={name} className="flex items-center justify-between gap-2 rounded-md bg-surface-2 px-3 py-2">
              <span className="text-base text-text-primary">{name}</span>
              <span className="flex gap-2">
                <button
                  type="button"
                  onClick={() => handleLoad(name)}
                  className="rounded-md bg-accent-muted px-3 py-2 text-sm font-medium text-text-primary"
                >
                  読込
                </button>
                <button
                  type="button"
                  onClick={() => handleRemove(name)}
                  className="rounded-md bg-danger/20 px-3 py-2 text-sm font-medium text-danger"
                >
                  削除
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
    </Section>
  )
}
