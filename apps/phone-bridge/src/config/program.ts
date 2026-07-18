/**
 * Pure helpers for building a bus's `layers[]` (§4.2 `ProgramRequest`,
 * back-to-front composited layer stack) and the request payload sent to
 * `POST /api/v1/program`. Kept free of React for unit testability.
 */
import type { PgmBus, PipSettings, ProgramLayer, ProgramRequest } from '../protocol/types'

export function addLayer(layers: readonly ProgramLayer[], sourceId: string, pip: PipSettings): ProgramLayer[] {
  return [...layers, { source_id: sourceId, pip }]
}

export function removeLayer(layers: readonly ProgramLayer[], index: number): ProgramLayer[] {
  return layers.filter((_, i) => i !== index)
}

export function updateLayerPip(layers: readonly ProgramLayer[], index: number, pip: PipSettings): ProgramLayer[] {
  return layers.map((layer, i) => (i === index ? { ...layer, pip } : layer))
}

/** Moves the layer at `fromIndex` to `toIndex`, re-ordering the back-to-front stack. */
export function moveLayer(layers: readonly ProgramLayer[], fromIndex: number, toIndex: number): ProgramLayer[] {
  if (
    fromIndex < 0 ||
    fromIndex >= layers.length ||
    toIndex < 0 ||
    toIndex >= layers.length ||
    fromIndex === toIndex
  ) {
    return layers.slice()
  }
  const next = layers.slice()
  const [moved] = next.splice(fromIndex, 1)
  next.splice(toIndex, 0, moved)
  return next
}

/** Builds the `POST /api/v1/program` payload for `bus`. */
export function buildProgramRequest(bus: PgmBus, layers: readonly ProgramLayer[], take: boolean): ProgramRequest {
  return { bus, layers: layers.slice(), take }
}
