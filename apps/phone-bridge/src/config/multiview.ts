/**
 * Pure 4x4 multiview cell array helpers (§4.2 `MultiviewConfig`, always 16
 * cells long). Kept free of React so the grid/cell logic is unit testable
 * without a rendered component.
 */
import { MULTIVIEW_CELL_COUNT, isMultiviewCell, type MultiviewCell } from '../protocol/types'

const SRC_PREFIX = 'SRC:'

export function createEmptyMultiviewCells(): MultiviewCell[] {
  return Array.from({ length: MULTIVIEW_CELL_COUNT }, () => 'EMPTY')
}

/** Formats a source id as a `SRC:<id>` multiview cell. */
export function sourceCell(sourceId: string): MultiviewCell {
  return `${SRC_PREFIX}${sourceId}`
}

/** Extracts the source id from a `SRC:<id>` cell, or null for any other cell kind. */
export function parseSourceCell(cell: MultiviewCell): string | null {
  return cell.startsWith(SRC_PREFIX) ? cell.slice(SRC_PREFIX.length) : null
}

/** Returns a copy of `cells` with `index` replaced by `value`. */
export function setMultiviewCell(
  cells: readonly MultiviewCell[],
  index: number,
  value: MultiviewCell,
): MultiviewCell[] {
  if (index < 0 || index >= cells.length) {
    throw new RangeError(`multiview cell index out of range: ${index}`)
  }
  const next = cells.slice()
  next[index] = value
  return next
}

/** Pads/truncates an arbitrary-length cell list to exactly `MULTIVIEW_CELL_COUNT`, filling gaps with `EMPTY`. */
export function normalizeMultiviewCells(cells: readonly MultiviewCell[]): MultiviewCell[] {
  const next = cells.slice(0, MULTIVIEW_CELL_COUNT)
  while (next.length < MULTIVIEW_CELL_COUNT) next.push('EMPTY')
  return next
}

/** Validates a candidate cell list against the wire contract; returns human-readable errors (empty = valid). */
export function validateMultiviewCells(cells: readonly unknown[]): string[] {
  const errors: string[] = []
  if (cells.length !== MULTIVIEW_CELL_COUNT) {
    errors.push(`セル数は${MULTIVIEW_CELL_COUNT}である必要があります（現在 ${cells.length}）`)
  }
  cells.forEach((cell, index) => {
    if (!isMultiviewCell(cell)) {
      errors.push(`セル${index}の値が不正です: ${String(cell)}`)
    }
  })
  return errors
}
