import { describe, expect, it } from 'vitest'
import {
  createEmptyMultiviewCells,
  normalizeMultiviewCells,
  parseSourceCell,
  setMultiviewCell,
  sourceCell,
  validateMultiviewCells,
} from '../multiview'

describe('createEmptyMultiviewCells', () => {
  it('returns 16 EMPTY cells', () => {
    const cells = createEmptyMultiviewCells()
    expect(cells).toHaveLength(16)
    expect(cells.every((cell) => cell === 'EMPTY')).toBe(true)
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

describe('setMultiviewCell', () => {
  it('replaces one cell without mutating the input array', () => {
    const cells = createEmptyMultiviewCells()
    const next = setMultiviewCell(cells, 3, 'PGM1')

    expect(next[3]).toBe('PGM1')
    expect(cells[3]).toBe('EMPTY')
    expect(next).toHaveLength(16)
  })

  it('throws for an out-of-range index', () => {
    const cells = createEmptyMultiviewCells()
    expect(() => setMultiviewCell(cells, 16, 'PGM1')).toThrow(RangeError)
    expect(() => setMultiviewCell(cells, -1, 'PGM1')).toThrow(RangeError)
  })
})

describe('normalizeMultiviewCells', () => {
  it('pads a short list with EMPTY up to 16', () => {
    const next = normalizeMultiviewCells(['PGM1', 'PGM2'])
    expect(next).toHaveLength(16)
    expect(next[0]).toBe('PGM1')
    expect(next[1]).toBe('PGM2')
    expect(next.slice(2).every((cell) => cell === 'EMPTY')).toBe(true)
  })

  it('truncates a long list down to 16', () => {
    const long = Array.from({ length: 20 }, () => 'PVW1' as const)
    expect(normalizeMultiviewCells(long)).toHaveLength(16)
  })
})

describe('validateMultiviewCells', () => {
  it('accepts a valid 16-cell list', () => {
    const cells = createEmptyMultiviewCells()
    cells[0] = 'PGM1'
    cells[1] = sourceCell('src-1')
    expect(validateMultiviewCells(cells)).toEqual([])
  })

  it('flags a wrong-length list', () => {
    expect(validateMultiviewCells(['PGM1'])).toContain('セル数は16である必要があります（現在 1）')
  })

  it('flags invalid cell values', () => {
    const cells: unknown[] = createEmptyMultiviewCells()
    cells[4] = 'NOT_A_CELL'
    const errors = validateMultiviewCells(cells)
    expect(errors).toContain('セル4の値が不正です: NOT_A_CELL')
  })

  it('rejects an empty SRC: id', () => {
    const cells = createEmptyMultiviewCells()
    cells[0] = 'SRC:'
    expect(validateMultiviewCells(cells).length).toBeGreaterThan(0)
  })
})
