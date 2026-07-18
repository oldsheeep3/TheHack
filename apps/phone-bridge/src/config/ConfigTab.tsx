import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import { ApiClient } from '../protocol/apiClient'
import type { MultiviewCell, PgmBus, ProgramLayer, SourceInfo, TallyState } from '../protocol/types'
import { createEmptyMultiviewCells } from './multiview'
import { ModulesPanel } from './ModulesPanel'
import { MultiviewPanel } from './MultiviewPanel'
import { OutputsPanel } from './OutputsPanel'
import { PicoNetworkPanel } from './PicoNetworkPanel'
import { LocalStoragePresetStore, type ScenePreset } from './presets'
import { PresetsPanel } from './PresetsPanel'
import { ProgramPanel } from './ProgramPanel'
import { SourcesPanel } from './SourcesPanel'
import { loadSourceOrder, mergeOrder, moveIdInOrder, saveSourceOrder, sortSourcesByOrder } from './sourceOrder'
import { useStableInstance } from './useStableInstance'

const SOURCES_POLL_MS = 3000
const TALLY_POLL_MS = 300

export function ConfigTab(): JSX.Element {
  const apiClient = useStableInstance(() => new ApiClient())
  const presetStore = useStableInstance(() => new LocalStoragePresetStore())

  const [rawSources, setRawSources] = useState<SourceInfo[]>([])
  const [order, setOrder] = useState<string[]>(() => loadSourceOrder())
  const [tally, setTally] = useState<TallyState | null>(null)
  const [programs, setPrograms] = useState<Record<PgmBus, ProgramLayer[]>>({ PGM1: [], PGM2: [] })
  const [multiview, setMultiview] = useState<MultiviewCell[]>(createEmptyMultiviewCells())
  const [lastError, setLastError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const handleError = (message: string): void => setLastError(message)
  const triggerRefresh = (): void => setRefreshToken((token) => token + 1)

  useEffect(() => {
    let cancelled = false
    const poll = (): void => {
      apiClient
        .getSources()
        .then((result) => {
          if (cancelled) return
          setRawSources(result)
          const ids = result.map((source) => source.id).filter((id): id is string => id !== null)
          setOrder((prev) => {
            const merged = mergeOrder(prev, ids)
            saveSourceOrder(merged)
            return merged
          })
        })
        .catch((error: unknown) => {
          if (!cancelled) handleError(error instanceof Error ? error.message : String(error))
        })
    }
    poll()
    const timer = setInterval(poll, SOURCES_POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [apiClient, refreshToken])

  useEffect(() => {
    let cancelled = false
    const poll = (): void => {
      apiClient
        .getTallyState()
        .then((result) => {
          if (!cancelled) setTally(result)
        })
        .catch(() => {
          // Tally is a best-effort overlay; a poll failure just leaves the last known state.
        })
    }
    poll()
    const timer = setInterval(poll, TALLY_POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [apiClient])

  const sources = sortSourcesByOrder(rawSources, order)

  const handleReorder = (id: string, direction: 'up' | 'down'): void => {
    setOrder((prev) => {
      const next = moveIdInOrder(prev, id, direction)
      saveSourceOrder(next)
      return next
    })
  }

  const handleLayersChange = (bus: PgmBus, layers: ProgramLayer[]): void => {
    setPrograms((prev) => ({ ...prev, [bus]: layers }))
  }

  const handleLoadPreset = (preset: ScenePreset): void => {
    setPrograms(preset.programs)
    setMultiview(preset.multiview)
    for (const bus of Object.keys(preset.programs) as PgmBus[]) {
      apiClient
        .applyProgram({ bus, layers: preset.programs[bus], take: false })
        .catch((error: unknown) => handleError(error instanceof Error ? error.message : String(error)))
    }
    apiClient
      .setMultiview({ cells: preset.multiview })
      .catch((error: unknown) => handleError(error instanceof Error ? error.message : String(error)))
  }

  return (
    <div className="flex flex-col gap-4">
      {lastError && (
        <p className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">{lastError}</p>
      )}

      <SourcesPanel
        apiClient={apiClient}
        sources={sources}
        tally={tally}
        onReorder={handleReorder}
        onChanged={triggerRefresh}
        onError={handleError}
      />

      <ProgramPanel
        apiClient={apiClient}
        sources={sources}
        programs={programs}
        onLayersChange={handleLayersChange}
        onError={handleError}
      />

      <MultiviewPanel
        apiClient={apiClient}
        sources={sources}
        tally={tally}
        cells={multiview}
        onCellsChange={setMultiview}
        onError={handleError}
      />

      <OutputsPanel apiClient={apiClient} onError={handleError} />

      <ModulesPanel apiClient={apiClient} sources={sources} onError={handleError} />

      <PicoNetworkPanel apiClient={apiClient} onError={handleError} />

      <PresetsPanel presetStore={presetStore} programs={programs} multiview={multiview} onLoad={handleLoadPreset} />
    </div>
  )
}
