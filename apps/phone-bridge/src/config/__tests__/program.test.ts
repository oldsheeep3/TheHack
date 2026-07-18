import { describe, expect, it } from 'vitest'
import type { PipSettings, ProgramLayer } from '../../protocol/types'
import { addLayer, buildProgramRequest, moveLayer, removeLayer, updateLayerPip } from '../program'

const pip: PipSettings = {
  enabled: true,
  x_position: 0,
  y_position: 0,
  width: 1920,
  height: 1080,
  opacity: 1,
  z_order: 0,
  crop: null,
}

const layers: ProgramLayer[] = [
  { source_id: 'src-a', pip },
  { source_id: 'src-b', pip: { ...pip, z_order: 1 } },
]

describe('addLayer', () => {
  it('appends a new layer without mutating the input', () => {
    const next = addLayer(layers, 'src-c', pip)
    expect(next).toHaveLength(3)
    expect(next[2]).toEqual({ source_id: 'src-c', pip })
    expect(layers).toHaveLength(2)
  })
})

describe('removeLayer', () => {
  it('removes the layer at the given index', () => {
    const next = removeLayer(layers, 0)
    expect(next).toEqual([layers[1]])
  })
})

describe('updateLayerPip', () => {
  it('replaces the pip for one layer, leaving others untouched', () => {
    const newPip = { ...pip, opacity: 0.5 }
    const next = updateLayerPip(layers, 1, newPip)
    expect(next[0]).toBe(layers[0])
    expect(next[1]).toEqual({ source_id: 'src-b', pip: newPip })
  })
})

describe('moveLayer', () => {
  it('reorders the back-to-front stack', () => {
    const three: ProgramLayer[] = [...layers, { source_id: 'src-c', pip }]
    const next = moveLayer(three, 0, 2)
    expect(next.map((l) => l.source_id)).toEqual(['src-b', 'src-c', 'src-a'])
  })

  it('is a no-op for an out-of-range index', () => {
    expect(moveLayer(layers, 0, 5)).toEqual(layers)
    expect(moveLayer(layers, -1, 0)).toEqual(layers)
  })
})

describe('buildProgramRequest', () => {
  it('formats the POST /api/v1/program payload for a bus', () => {
    expect(buildProgramRequest('PGM1', layers, false)).toEqual({
      bus: 'PGM1',
      layers,
      take: false,
    })
  })

  it('sets take: true when requested', () => {
    expect(buildProgramRequest('PGM2', [], true)).toEqual({ bus: 'PGM2', layers: [], take: true })
  })

  it('copies the layers array rather than aliasing it', () => {
    const request = buildProgramRequest('PGM1', layers, false)
    expect(request.layers).not.toBe(layers)
  })
})
