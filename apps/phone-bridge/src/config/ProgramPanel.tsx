import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import { PGM_BUSES, type PgmBus, type PipSettings, type ProgramLayer, type SourceInfo } from '../protocol/types'
import { debounce } from './debounce'
import { PipEditor } from './PipEditor'
import { addLayer, buildProgramRequest, moveLayer, removeLayer, updateLayerPip } from './program'
import { Section } from './ui'
import { useStableInstance } from './useStableInstance'

const PROGRAM_SEND_DEBOUNCE_MS = 150

const DEFAULT_PIP: PipSettings = {
  enabled: true,
  x_position: 0,
  y_position: 0,
  width: 640,
  height: 360,
  opacity: 1,
  z_order: 0,
  crop: null,
}

interface ProgramPanelProps {
  apiClient: ApiClient
  sources: SourceInfo[]
  programs: Record<PgmBus, ProgramLayer[]>
  onLayersChange: (bus: PgmBus, layers: ProgramLayer[]) => void
  onError: (message: string) => void
}

export function ProgramPanel({ apiClient, sources, programs, onLayersChange, onError }: ProgramPanelProps): JSX.Element {
  const [activeBus, setActiveBus] = useState<PgmBus>('PGM1')
  const [selectedLayerIndex, setSelectedLayerIndex] = useState<number | null>(null)
  const [addSourceId, setAddSourceId] = useState<string>('')

  const debouncers = useStableInstance(() =>
    Object.fromEntries(
      PGM_BUSES.map((bus) => [
        bus,
        debounce((layers: ProgramLayer[]) => {
          apiClient
            .applyProgram(buildProgramRequest(bus, layers, false))
            .catch((error: unknown) => onError(error instanceof Error ? error.message : String(error)))
        }, PROGRAM_SEND_DEBOUNCE_MS),
      ]),
    ) as Record<PgmBus, ReturnType<typeof debounce<[ProgramLayer[]]>>>,
  )

  const activeLayers = programs[activeBus]

  const commitLayers = (layers: ProgramLayer[]): void => {
    onLayersChange(activeBus, layers)
    debouncers[activeBus](layers)
  }

  const handleAddLayer = (): void => {
    if (!addSourceId) return
    const next = addLayer(activeLayers, addSourceId, DEFAULT_PIP)
    commitLayers(next)
    setSelectedLayerIndex(next.length - 1)
    setAddSourceId('')
  }

  const handleRemoveLayer = (index: number): void => {
    commitLayers(removeLayer(activeLayers, index))
    setSelectedLayerIndex(null)
  }

  const handleMoveLayer = (index: number, direction: 'up' | 'down'): void => {
    const target = direction === 'up' ? index - 1 : index + 1
    commitLayers(moveLayer(activeLayers, index, target))
    setSelectedLayerIndex(target >= 0 && target < activeLayers.length ? target : index)
  }

  const handlePipChange = (pip: PipSettings): void => {
    if (selectedLayerIndex === null) return
    commitLayers(updateLayerPip(activeLayers, selectedLayerIndex, pip))
  }

  const handleTake = (): void => {
    apiClient
      .applyProgram(buildProgramRequest(activeBus, activeLayers, true))
      .catch((error: unknown) => onError(error instanceof Error ? error.message : String(error)))
  }

  const sourceName = (id: string): string => sources.find((source) => source.id === id)?.name ?? id
  const selectedLayer = selectedLayerIndex !== null ? activeLayers[selectedLayerIndex] : null

  return (
    <Section
      title="2系統ME プログラム＋PiP編集"
      actions={
        <button
          type="button"
          onClick={handleTake}
          className="rounded-md bg-danger px-4 py-2 text-sm font-semibold text-white"
        >
          TAKE ({activeBus})
        </button>
      }
    >
      <div className="mb-3 flex gap-2" role="tablist" aria-label="プログラムバス切替">
        {PGM_BUSES.map((bus) => (
          <button
            key={bus}
            type="button"
            role="tab"
            aria-selected={activeBus === bus}
            onClick={() => {
              setActiveBus(bus)
              setSelectedLayerIndex(null)
            }}
            className={`flex-1 rounded-md px-3 py-2 text-sm font-medium ${
              activeBus === bus ? 'bg-accent-muted text-text-primary ring-1 ring-accent' : 'bg-surface-2 text-text-muted'
            }`}
          >
            {bus}
          </button>
        ))}
      </div>

      <div className="mb-3 flex gap-2">
        <select
          value={addSourceId}
          onChange={(event) => setAddSourceId(event.target.value)}
          className="flex-1 rounded-md border border-border bg-surface-2 px-3 py-2 text-sm text-text-primary"
        >
          <option value="">レイヤーに追加するソースを選択…</option>
          {sources
            .filter((source) => source.id !== null)
            .map((source) => (
              <option key={source.id} value={source.id as string}>
                {source.name}
              </option>
            ))}
        </select>
        <button
          type="button"
          disabled={!addSourceId}
          onClick={handleAddLayer}
          className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-white disabled:opacity-40"
        >
          + レイヤー
        </button>
      </div>

      {activeLayers.length === 0 ? (
        <p className="text-sm text-text-muted">レイヤーがありません（背面から追加してください）。</p>
      ) : (
        <ul className="mb-3 flex flex-col gap-2">
          {activeLayers.map((layer, index) => (
            <li
              key={`${layer.source_id}-${index}`}
              className={`flex items-center justify-between gap-2 rounded-md px-3 py-2 ${
                selectedLayerIndex === index ? 'bg-accent-muted ring-1 ring-accent' : 'bg-surface-2'
              }`}
            >
              <button
                type="button"
                onClick={() => setSelectedLayerIndex(index)}
                className="flex-1 text-left text-sm text-text-primary"
              >
                #{index} {sourceName(layer.source_id)}
              </button>
              <span className="flex gap-1">
                <button
                  type="button"
                  onClick={() => handleMoveLayer(index, 'up')}
                  aria-label="背面へ"
                  className="rounded-md bg-surface-1 px-2 py-1 text-xs"
                >
                  ↑
                </button>
                <button
                  type="button"
                  onClick={() => handleMoveLayer(index, 'down')}
                  aria-label="前面へ"
                  className="rounded-md bg-surface-1 px-2 py-1 text-xs"
                >
                  ↓
                </button>
                <button
                  type="button"
                  onClick={() => handleRemoveLayer(index)}
                  className="rounded-md bg-danger/20 px-2 py-1 text-xs font-medium text-danger"
                >
                  削除
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      {selectedLayer && (
        <div className="rounded-lg border border-border p-3">
          <p className="mb-2 text-sm font-medium text-text-primary">
            {sourceName(selectedLayer.source_id)} の PiPレイアウト
          </p>
          <PipEditor pip={selectedLayer.pip} onChange={handlePipChange} />
        </div>
      )}
    </Section>
  )
}
