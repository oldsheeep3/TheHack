/**
 * Pure multiview layout helpers (§4.2 `MultiviewLayout`).
 *
 * Two wire forms exist. `cells` is the legacy fixed 4x4 (16 entries); `grid` + `regions` is what the
 * PC's own multiview editor writes — an arbitrary grid whose regions may span several cells. Everything
 * here works in the region form and converts the legacy one on load, so the client edits a single
 * representation. Kept free of React so the grid/merge logic is unit testable.
 */
import {
  MULTIVIEW_MAX_GRID,
  MULTIVIEW_MIN_GRID,
  isLegacyMultiviewCells,
  isMultiviewCell,
  type MultiviewCell,
  type MultiviewGrid,
  type MultiviewLayout,
  type MultiviewRegion,
} from '../protocol/types'

const SRC_PREFIX = 'SRC:'

/** Columns the legacy 16-cell form implies. */
export const LEGACY_COLUMNS = 4

export const DEFAULT_GRID: MultiviewGrid = { rows: 4, cols: 4 }

/** Grid sizes the PC accepts, for the rows/cols pickers. */
export const GRID_SIZES: readonly number[] = Array.from(
  { length: MULTIVIEW_MAX_GRID - MULTIVIEW_MIN_GRID + 1 },
  (_, i) => MULTIVIEW_MIN_GRID + i,
)

/** Formats a source id as a `SRC:<id>` multiview cell. */
export function sourceCell(sourceId: string): MultiviewCell {
  return `${SRC_PREFIX}${sourceId}`
}

/** Extracts the source id from a `SRC:<id>` cell, or null for any other cell kind. */
export function parseSourceCell(cell: MultiviewCell): string | null {
  return cell.startsWith(SRC_PREFIX) ? cell.slice(SRC_PREFIX.length) : null
}

/** A grid of 1x1 EMPTY regions. */
export function createGridRegions(grid: MultiviewGrid): MultiviewRegion[] {
  const regions: MultiviewRegion[] = []
  for (let row = 0; row < grid.rows; row++) {
    for (let col = 0; col < grid.cols; col++) {
      regions.push({ row, col, row_span: 1, col_span: 1, content: 'EMPTY' })
    }
  }
  return regions
}

/** Expands the legacy 16-cell form into 1x1 regions across `LEGACY_COLUMNS` columns. */
export function regionsFromCells(cells: readonly MultiviewCell[]): MultiviewRegion[] {
  return cells.map((content, index) => ({
    row: Math.floor(index / LEGACY_COLUMNS),
    col: index % LEGACY_COLUMNS,
    row_span: 1,
    col_span: 1,
    content,
  }))
}

/**
 * Normalizes whatever `GET /api/v1/multiview` returned into the region form. A PC that has never had a
 * layout applied answers with the 16-cell default, and one written by an older build only ever has that
 * form — neither should leave the editor blank.
 */
export function layoutToRegions(layout: MultiviewLayout | null | undefined): {
  grid: MultiviewGrid
  regions: MultiviewRegion[]
} {
  if (layout?.regions && layout.regions.length > 0) {
    return {
      grid: layout.grid ?? DEFAULT_GRID,
      regions: sortRegions(layout.regions.map((region) => ({ ...region }))),
    }
  }

  if (isLegacyMultiviewCells(layout)) {
    return { grid: DEFAULT_GRID, regions: regionsFromCells(layout.cells) }
  }

  return { grid: DEFAULT_GRID, regions: createGridRegions(DEFAULT_GRID) }
}

/** The payload for `PUT /api/v1/multiview`. Never carries both forms — the PC rejects that. */
export function toMultiviewLayout(grid: MultiviewGrid, regions: readonly MultiviewRegion[]): MultiviewLayout {
  return { grid, regions: regions.map((region) => ({ ...region })) }
}

/** Index of the region covering `(row, col)`, or -1 when the cell is uncovered. */
export function regionIndexAt(regions: readonly MultiviewRegion[], row: number, col: number): number {
  return regions.findIndex((region) => covers(region, row, col))
}

/** Returns a copy of `regions` with the region at `index` assigned `content`. */
export function setRegionContent(
  regions: readonly MultiviewRegion[],
  index: number,
  content: MultiviewCell,
): MultiviewRegion[] {
  if (index < 0 || index >= regions.length) {
    throw new RangeError(`multiview region index out of range: ${index}`)
  }
  return regions.map((region, i) => (i === index ? { ...region, content } : region))
}

/**
 * Merges the selected regions into their bounding rectangle, keeping the top-left one's content.
 * Returns null when that rectangle would cut through a region that is not itself selected: the PC
 * requires regions to tile the grid exactly, so a partial overlap has no valid result.
 */
export function mergeRegions(
  regions: readonly MultiviewRegion[],
  selected: readonly number[],
): MultiviewRegion[] | null {
  const picked = selected
    .map((index) => regions[index])
    .filter((region): region is MultiviewRegion => region !== undefined)
  if (picked.length < 2) return null

  const top = Math.min(...picked.map((r) => r.row))
  const left = Math.min(...picked.map((r) => r.col))
  const bottom = Math.max(...picked.map((r) => r.row + r.row_span))
  const right = Math.max(...picked.map((r) => r.col + r.col_span))

  const kept: MultiviewRegion[] = []
  for (const region of regions) {
    if (isInside(region, top, left, bottom, right)) continue
    if (overlaps(region, top, left, bottom, right)) return null
    kept.push({ ...region })
  }

  const topLeft = [...picked].sort((a, b) => a.row - b.row || a.col - b.col)[0]
  kept.push({ row: top, col: left, row_span: bottom - top, col_span: right - left, content: topLeft.content })
  return sortRegions(kept)
}

/** True when merging `selected` would produce a valid tiling. */
export function canMergeRegions(regions: readonly MultiviewRegion[], selected: readonly number[]): boolean {
  return mergeRegions(regions, selected) !== null
}

/**
 * Splits the region at `index` back into 1x1 cells. The content stays on the top-left cell rather than
 * being copied into all of them: a split is undoing a merge, not multiplying the feed.
 */
export function splitRegion(regions: readonly MultiviewRegion[], index: number): MultiviewRegion[] {
  const target = regions[index]
  if (!target) throw new RangeError(`multiview region index out of range: ${index}`)
  if (target.row_span === 1 && target.col_span === 1) return regions.map((region) => ({ ...region }))

  const rest = regions.filter((_, i) => i !== index).map((region) => ({ ...region }))
  for (let row = target.row; row < target.row + target.row_span; row++) {
    for (let col = target.col; col < target.col + target.col_span; col++) {
      const isTopLeft = row === target.row && col === target.col
      rest.push({ row, col, row_span: 1, col_span: 1, content: isTopLeft ? target.content : 'EMPTY' })
    }
  }
  return sortRegions(rest)
}

/**
 * Re-fits `regions` to a new grid size. Regions that still fit whole are kept — so growing the grid does
 * not throw away merges — and any cell left uncovered becomes a 1x1 EMPTY.
 */
export function resizeGrid(regions: readonly MultiviewRegion[], grid: MultiviewGrid): MultiviewRegion[] {
  const kept = regions
    .filter((region) => region.row + region.row_span <= grid.rows && region.col + region.col_span <= grid.cols)
    .map((region) => ({ ...region }))

  for (let row = 0; row < grid.rows; row++) {
    for (let col = 0; col < grid.cols; col++) {
      if (!kept.some((region) => covers(region, row, col))) {
        kept.push({ row, col, row_span: 1, col_span: 1, content: 'EMPTY' })
      }
    }
  }

  return sortRegions(kept)
}

/**
 * Validates a layout against the same rules as the PC's `MultiviewLayoutValidator`: the grid within
 * bounds, every region a rectangle inside it, and the grid covered exactly once. Returns human-readable
 * errors (empty = valid), so a layout that passes here is not bounced by the API.
 */
export function validateRegions(grid: MultiviewGrid, regions: readonly MultiviewRegion[]): string[] {
  const errors: string[] = []

  if (
    grid.rows < MULTIVIEW_MIN_GRID ||
    grid.rows > MULTIVIEW_MAX_GRID ||
    grid.cols < MULTIVIEW_MIN_GRID ||
    grid.cols > MULTIVIEW_MAX_GRID
  ) {
    errors.push(
      `グリッドは${MULTIVIEW_MIN_GRID}〜${MULTIVIEW_MAX_GRID}行×${MULTIVIEW_MIN_GRID}〜${MULTIVIEW_MAX_GRID}列にしてください（現在 ${grid.rows}x${grid.cols}）`,
    )
    return errors
  }

  if (regions.length === 0) {
    errors.push('領域が1つもありません。')
    return errors
  }

  const covered: number[][] = Array.from({ length: grid.rows }, () => Array.from({ length: grid.cols }, () => 0))

  regions.forEach((region, index) => {
    if (!isMultiviewCell(region.content)) {
      errors.push(`領域${index}の割当が不正です: ${String(region.content)}`)
    }

    if (
      region.row_span <= 0 ||
      region.col_span <= 0 ||
      region.row < 0 ||
      region.col < 0 ||
      region.row + region.row_span > grid.rows ||
      region.col + region.col_span > grid.cols
    ) {
      errors.push(`領域${index}がグリッド（${grid.rows}x${grid.cols}）の外に出ています。`)
      return
    }

    for (let row = region.row; row < region.row + region.row_span; row++) {
      for (let col = region.col; col < region.col + region.col_span; col++) {
        covered[row][col] += 1
      }
    }
  })

  for (let row = 0; row < grid.rows; row++) {
    for (let col = 0; col < grid.cols; col++) {
      if (covered[row][col] > 1) {
        errors.push(`セル(${row}, ${col})が複数の領域に重なっています。`)
      } else if (covered[row][col] === 0) {
        errors.push(`セル(${row}, ${col})がどの領域にも含まれていません。`)
      }
    }
  }

  return errors
}

/** Row-major order, so a re-tiled layout renders in the same order it is read. */
export function sortRegions(regions: readonly MultiviewRegion[]): MultiviewRegion[] {
  return [...regions].sort((a, b) => a.row - b.row || a.col - b.col)
}

function covers(region: MultiviewRegion, row: number, col: number): boolean {
  return (
    row >= region.row &&
    row < region.row + region.row_span &&
    col >= region.col &&
    col < region.col + region.col_span
  )
}

function isInside(region: MultiviewRegion, top: number, left: number, bottom: number, right: number): boolean {
  return (
    region.row >= top &&
    region.col >= left &&
    region.row + region.row_span <= bottom &&
    region.col + region.col_span <= right
  )
}

function overlaps(region: MultiviewRegion, top: number, left: number, bottom: number, right: number): boolean {
  return !(
    region.row + region.row_span <= top ||
    region.row >= bottom ||
    region.col + region.col_span <= left ||
    region.col >= right
  )
}
