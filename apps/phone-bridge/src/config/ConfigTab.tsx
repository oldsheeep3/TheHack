import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import { ApiClient } from '../protocol/apiClient'
import type {
  MultiviewGrid,
  MultiviewRegion,
  PgmBus,
  ProgramLayer,
  SourceDefinition,
  SourceInfo,
} from '../protocol/types'
import { AtemPanel } from './AtemPanel'
import { AudioPanel } from './AudioPanel'
import { DEFAULT_GRID, createGridRegions, layoutToRegions, toMultiviewLayout } from './multiview'
import { ModulesPanel } from './ModulesPanel'
import { MultiviewPanel } from './MultiviewPanel'
import { OutputsPanel } from './OutputsPanel'
import { PicoNetworkPanel } from './PicoNetworkPanel'
import { LocalStoragePresetStore, type ScenePreset } from './presets'
import { PresetsPanel } from './PresetsPanel'
import { ProgramPanel } from './ProgramPanel'
import { SourcesPanel } from './SourcesPanel'
import { loadHostConfig } from '../net/hostConfig'
import { loadSourceOrder, mergeOrder, moveIdInOrder, saveSourceOrder, sortSourcesByOrder } from './sourceOrder'
import { useStableInstance } from './useStableInstance'

const SOURCES_POLL_MS = 3000

export function ConfigTab(): JSX.Element {
  // Same target the connection tab uses: its stored override when there is one, otherwise this page's
  // own origin (the PC serves this UI from the port the API is on). Read at mount, and this component
  // unmounts when the operator switches to the connection tab, so a new target takes effect on return.
  const apiClient = useStableInstance(() => new ApiClient(loadHostConfig() ?? {}))
  const presetStore = useStableInstance(() => new LocalStoragePresetStore())

  const [rawSources, setRawSources] = useState<SourceInfo[]>([])
  const [definitions, setDefinitions] = useState<Map<string, SourceDefinition>>(new Map())
  const [order, setOrder] = useState<string[]>(() => loadSourceOrder())
  const [programs, setPrograms] = useState<Record<PgmBus, ProgramLayer[]>>({ PGM1: [], PGM2: [] })
  const [multiviewGrid, setMultiviewGrid] = useState<MultiviewGrid>(DEFAULT_GRID)
  const [multiviewRegions, setMultiviewRegions] = useState<MultiviewRegion[]>(() => createGridRegions(DEFAULT_GRID))
  const [multiviewLoaded, setMultiviewLoaded] = useState(false)
  const [lastError, setLastError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const handleError = (message: string): void => setLastError(message)
  const triggerRefresh = (): void => setRefreshToken((token) => token + 1)

  // The list and the definitions are polled together: the list is what the PC's engine is running, the
  // definitions are what an edit form needs, and a source that appears in one without the other would
  // show an "編集" button that cannot be honoured.
  useEffect(() => {
    let cancelled = false
    const poll = (): void => {
      Promise.all([apiClient.getSources(), apiClient.getSourceDefinitions()])
        .then(([sourceList, definitionList]) => {
          if (cancelled) return
          setRawSources(sourceList)
          setDefinitions(new Map(definitionList.map((definition) => [definition.id, definition])))
          const ids = sourceList.map((source) => source.id).filter((id): id is string => id !== null)
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

  // Loaded once rather than polled: this is what the operator is editing below.
  useEffect(() => {
    const controller = new AbortController()
    apiClient
      .getMultiview(controller.signal)
      .then((layout) => {
        const normalized = layoutToRegions(layout)
        setMultiviewGrid(normalized.grid)
        setMultiviewRegions(normalized.regions)
        setMultiviewLoaded(true)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        handleError(error instanceof Error ? error.message : String(error))
      })
    return () => controller.abort()
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

  const handleMultiviewChange = (grid: MultiviewGrid, regions: MultiviewRegion[]): void => {
    setMultiviewGrid(grid)
    setMultiviewRegions(regions)
  }

  const handleLoadPreset = (preset: ScenePreset): void => {
    setPrograms(preset.programs)
    setMultiviewGrid(preset.multiviewGrid)
    setMultiviewRegions(preset.multiviewRegions)
    for (const bus of Object.keys(preset.programs) as PgmBus[]) {
      apiClient
        .applyProgram({ bus, layers: preset.programs[bus], take: false })
        .catch((error: unknown) => handleError(error instanceof Error ? error.message : String(error)))
    }
    apiClient
      .setMultiview(toMultiviewLayout(preset.multiviewGrid, preset.multiviewRegions))
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
        definitions={definitions}
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
        grid={multiviewGrid}
        regions={multiviewRegions}
        onLayoutChange={handleMultiviewChange}
        loaded={multiviewLoaded}
        onError={handleError}
      />

      <OutputsPanel apiClient={apiClient} onError={handleError} />

      <AudioPanel apiClient={apiClient} onError={handleError} />

      <ModulesPanel apiClient={apiClient} sources={sources} onError={handleError} />

      <AtemPanel apiClient={apiClient} onError={handleError} />

      <PicoNetworkPanel apiClient={apiClient} onError={handleError} />

      <PresetsPanel
        presetStore={presetStore}
        programs={programs}
        multiviewGrid={multiviewGrid}
        multiviewRegions={multiviewRegions}
        onLoad={handleLoadPreset}
      />
    </div>
  )
}
