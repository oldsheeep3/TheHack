import { describe, expect, it } from 'vitest'
import type { MultiviewCell, MultiviewGrid, MultiviewRegion } from '../../protocol/types'
import {
  DEFAULT_GRID,
  canMergeRegions,
  createGridRegions,
  layoutToRegions,
  mergeRegions,
  parseSourceCell,
  regionIndexAt,
  regionsFromCells,
  resizeGrid,
  setRegionContent,
  sourceCell,
  splitRegion,
  toMultiviewLayout,
  validateRegions,
} from '../multiview'

const GRID_4X4: MultiviewGrid = { rows: 4, cols: 4 }

function at(regions: MultiviewRegion[], row: number, col: number): number {
  return regionIndexAt(regions, row, col)
}

function emptyCells(): MultiviewCell[] {
  return Array.from({ length: 16 }, () => 'EMPTY')
}

describe('createGridRegions', () => {
  it('tiles the grid with 1x1 EMPTY regions', () => {
    const regions = createGridRegions(GRID_4X4)
    expect(regions).toHaveLength(16)
    expect(regions.every((region) => region.row_span === 1 && region.col_span === 1)).toBe(true)
    expect(regions.every((region) => region.content === 'EMPTY')).toBe(true)
    expect(validateRegions(GRID_4X4, regions)).toEqual([])
  })
})

describe('sourceCell / parseSourceCell', () => {
  it('formats and parses a SRC:<id> cell', () => {
    expect(sourceCell('src-ndi-cam1')).toBe('SRC:src-ndi-cam1')
    expect(parseSourceCell('SRC:src-ndi-cam1')).toBe('src-ndi-cam1')
  })

  it('returns null for non-SRC cells', () => {
    expect(parseSourceCell('PGM1')).toBeNull()
    expect(parseSourceCell('EMPTY')).toBeNull()
  })
})

describe('layoutToRegions', () => {
  it('expands the legacy 16-cell form into a 4x4 of 1x1 regions', () => {
    const cells = emptyCells()
    cells[5] = 'PGM1'

    const { grid, regions } = layoutToRegions({ cells })

    expect(grid).toEqual(DEFAULT_GRID)
    expect(regions).toHaveLength(16)
    expect(regions[at(regions, 1, 1)].content).toBe('PGM1')
  })

  it('keeps the region form as-is, spans included', () => {
    const regions = regionsFromCells(emptyCells())
    const merged = mergeRegions(regions, [0, 1, 4, 5])!

    const restored = layoutToRegions({ grid: GRID_4X4, regions: merged })

    expect(restored.grid).toEqual(GRID_4X4)
    expect(restored.regions).toEqual(merged)
  })

  it('falls back to an empty 4x4 for a layout carrying neither form', () => {
    expect(layoutToRegions(null).regions).toHaveLength(16)
    expect(layoutToRegions({}).grid).toEqual(DEFAULT_GRID)
  })
})

describe('setRegionContent', () => {
  it('replaces one region without mutating the input', () => {
    const regions = createGridRegions(GRID_4X4)
    const next = setRegionContent(regions, 3, 'PGM1')

    expect(next[3].content).toBe('PGM1')
    expect(regions[3].content).toBe('EMPTY')
  })

  it('throws for an out-of-range index', () => {
    const regions = createGridRegions(GRID_4X4)
    expect(() => setRegionContent(regions, 16, 'PGM1')).toThrow(RangeError)
    expect(() => setRegionContent(regions, -1, 'PGM1')).toThrow(RangeError)
  })
})

describe('mergeRegions', () => {
  it('merges a 2x2 block into one region carrying the top-left content', () => {
    const regions = setRegionContent(createGridRegions(GRID_4X4), 0, 'PGM1')

    const merged = mergeRegions(regions, [
      at(regions, 0, 0),
      at(regions, 0, 1),
      at(regions, 1, 0),
      at(regions, 1, 1),
    ])!

    expect(merged).toHaveLength(13)
    expect(merged[at(merged, 0, 0)]).toMatchObject({ row: 0, col: 0, row_span: 2, col_span: 2, content: 'PGM1' })
    expect(validateRegions(GRID_4X4, merged)).toEqual([])
  })

  it('absorbs the cells a diagonal selection encloses, since the merge is by bounding box', () => {
    const regions = createGridRegions(GRID_4X4)
    const diagonal = [at(regions, 0, 0), at(regions, 1, 1)]

    const merged = mergeRegions(regions, diagonal)!

    expect(merged[at(merged, 0, 0)]).toMatchObject({ row_span: 2, col_span: 2 })
    expect(validateRegions(GRID_4X4, merged)).toEqual([])
  })

  it('refuses a selection whose bounding box would cut through an unselected region', () => {
    const grid = createGridRegions(GRID_4X4)
    // A 2x2 merged block sitting in columns 1-2...
    const withBlock = mergeRegions(grid, [at(grid, 0, 1), at(grid, 0, 2), at(grid, 1, 1), at(grid, 1, 2)])!
    // ...and a selection whose bounding box reaches column 1 but stops short of column 2.
    const straddling = [at(withBlock, 0, 0), at(withBlock, 2, 1)]

    expect(mergeRegions(withBlock, straddling)).toBeNull()
    expect(canMergeRegions(withBlock, straddling)).toBe(false)
  })

  it('needs at least two regions', () => {
    const regions = createGridRegions(GRID_4X4)
    expect(mergeRegions(regions, [0])).toBeNull()
    expect(mergeRegions(regions, [])).toBeNull()
  })

  it('merges a merged region with its neighbours', () => {
    const regions = createGridRegions(GRID_4X4)
    const twoByTwo = mergeRegions(regions, [
      at(regions, 0, 0),
      at(regions, 0, 1),
      at(regions, 1, 0),
      at(regions, 1, 1),
    ])!

    const wider = mergeRegions(twoByTwo, [
      at(twoByTwo, 0, 0),
      at(twoByTwo, 0, 2),
      at(twoByTwo, 0, 3),
      at(twoByTwo, 1, 2),
      at(twoByTwo, 1, 3),
    ])!

    expect(wider[at(wider, 0, 0)]).toMatchObject({ row_span: 2, col_span: 4 })
    expect(validateRegions(GRID_4X4, wider)).toEqual([])
  })
})

describe('splitRegion', () => {
  it('splits a merged region back into 1x1 cells, keeping the content top-left', () => {
    const regions = setRegionContent(createGridRegions(GRID_4X4), 0, 'PGM1')
    const merged = mergeRegions(regions, [
      at(regions, 0, 0),
      at(regions, 0, 1),
      at(regions, 1, 0),
      at(regions, 1, 1),
    ])!

    const split = splitRegion(merged, at(merged, 0, 0))

    expect(split).toHaveLength(16)
    expect(split[at(split, 0, 0)].content).toBe('PGM1')
    expect(split[at(split, 0, 1)].content).toBe('EMPTY')
    expect(validateRegions(GRID_4X4, split)).toEqual([])
  })

  it('leaves a 1x1 region alone', () => {
    const regions = createGridRegions(GRID_4X4)
    expect(splitRegion(regions, 0)).toEqual(regions)
  })
})

describe('resizeGrid', () => {
  it('fills the new cells when the grid grows, keeping merges', () => {
    const regions = createGridRegions(GRID_4X4)
    const merged = mergeRegions(regions, [
      at(regions, 0, 0),
      at(regions, 0, 1),
      at(regions, 1, 0),
      at(regions, 1, 1),
    ])!

    const grown: MultiviewGrid = { rows: 5, cols: 5 }
    const resized = resizeGrid(merged, grown)

    expect(resized[at(resized, 0, 0)]).toMatchObject({ row_span: 2, col_span: 2 })
    expect(validateRegions(grown, resized)).toEqual([])
  })

  it('re-tiles what no longer fits when the grid shrinks', () => {
    const grown: MultiviewGrid = { rows: 6, cols: 6 }
    const regions = createGridRegions(grown)

    const resized = resizeGrid(regions, GRID_4X4)

    expect(resized).toHaveLength(16)
    expect(validateRegions(GRID_4X4, resized)).toEqual([])
  })
})

describe('validateRegions', () => {
  it('flags an out-of-bounds grid', () => {
    expect(validateRegions({ rows: 3, cols: 4 }, createGridRegions(GRID_4X4)).length).toBeGreaterThan(0)
  })

  it('flags overlapping regions', () => {
    const regions = createGridRegions(GRID_4X4)
    regions.push({ row: 0, col: 0, row_span: 2, col_span: 2, content: 'PGM1' })
    expect(validateRegions(GRID_4X4, regions).some((error) => error.includes('重なって'))).toBe(true)
  })

  it('flags an uncovered cell', () => {
    const regions = createGridRegions(GRID_4X4).slice(1)
    expect(validateRegions(GRID_4X4, regions).some((error) => error.includes('含まれていません'))).toBe(true)
  })
})

describe('toMultiviewLayout', () => {
  it('sends the region form only, since the PC rejects a payload carrying both', () => {
    const layout = toMultiviewLayout(GRID_4X4, createGridRegions(GRID_4X4))

    expect(layout.grid).toEqual(GRID_4X4)
    expect(layout.regions).toHaveLength(16)
    expect(layout.cells).toBeUndefined()
  })
})
