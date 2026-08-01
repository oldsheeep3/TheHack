import { useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import type { MultiviewCell, MultiviewGrid, MultiviewRegion, SourceInfo } from '../protocol/types'
import {
  GRID_SIZES,
  canMergeRegions,
  mergeRegions,
  parseSourceCell,
  regionIndexAt,
  resizeGrid,
  setRegionContent,
  splitRegion,
  toMultiviewLayout,
  validateRegions,
} from './multiview'
import { Field, Section, primaryButtonClass, secondaryButtonClass, selectClass } from './ui'

const FIXED_CELL_OPTIONS: MultiviewCell[] = ['EMPTY', 'PGM1', 'PGM2', 'PVW1', 'PVW2']

interface MultiviewPanelProps {
  apiClient: ApiClient
  sources: SourceInfo[]
  grid: MultiviewGrid
  regions: MultiviewRegion[]
  /** Lifted so scene presets can snapshot the layout alongside the program stacks. */
  onLayoutChange: (grid: MultiviewGrid, regions: MultiviewRegion[]) => void
  loaded: boolean
  onError: (message: string) => void
}

/**
 * Multiview layout editor (§4.2 / multiview-output-revision §4.1). The grid is any size the PC accepts
 * and regions may span cells, which is what the PC's own editor writes — a client stuck on a fixed 4x4
 * would flatten every merged region the moment it applied.
 */
export function MultiviewPanel({
  apiClient,
  sources,
  grid,
  regions,
  onLayoutChange,
  loaded,
  onError,
}: MultiviewPanelProps): JSX.Element {
  const [selected, setSelected] = useState<number[]>([])
  const [busy, setBusy] = useState(false)

  const errors = validateRegions(grid, regions)

  const setRegions = (next: MultiviewRegion[]): void => onLayoutChange(grid, next)

  const handleGridChange = (next: MultiviewGrid): void => {
    onLayoutChange(next, resizeGrid(regions, next))
    setSelected([])
  }

  const toggleSelected = (index: number): void => {
    setSelected((prev) => (prev.includes(index) ? prev.filter((i) => i !== index) : [...prev, index]))
  }

  const handleMerge = (): void => {
    const merged = mergeRegions(regions, selected)
    if (!merged) return
    setRegions(merged)
    setSelected([])
  }

  const handleSplit = (): void => {
    if (selected.length !== 1) return
    setRegions(splitRegion(regions, selected[0]))
    setSelected([])
  }

  const handleApply = async (): Promise<void> => {
    setBusy(true)
    try {
      await apiClient.setMultiview(toMultiviewLayout(grid, regions))
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  const describeCell = (cell: MultiviewCell): string => {
    const sourceId = parseSourceCell(cell)
    if (sourceId === null) return cell
    const source = sources.find((candidate) => candidate.id === sourceId)
    return source ? `SRC: ${source.name}` : cell
  }

  // Rendered as an explicit grid so a region's span is visible: a cell is drawn only where its region
  // starts, and the region occupies the rest via CSS spans.
  const cellsInReadingOrder: { row: number; col: number; index: number }[] = []
  for (let row = 0; row < grid.rows; row++) {
    for (let col = 0; col < grid.cols; col++) {
      const index = regionIndexAt(regions, row, col)
      const region = regions[index]
      if (region && region.row === row && region.col === col) {
        cellsInReadingOrder.push({ row, col, index })
      }
    }
  }

  return (
    <Section
      title="マルチビュー割当"
      hint="行数・列数の変更とセル結合に対応（PC側のマルチビュー設定と同じ形式で送信します）。"
      actions={
        <div className="flex items-center gap-1">
          <select
            value={grid.rows}
            onChange={(event) => handleGridChange({ ...grid, rows: Number(event.target.value) })}
            aria-label="行数"
            className={selectClass}
          >
            {GRID_SIZES.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
          <span className="text-xs text-text-muted">×</span>
          <select
            value={grid.cols}
            onChange={(event) => handleGridChange({ ...grid, cols: Number(event.target.value) })}
            aria-label="列数"
            className={selectClass}
          >
            {GRID_SIZES.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </div>
      }
    >
      {!loaded ? (
        <p className="text-sm text-text-muted">読み込み中…</p>
      ) : (
        <div className="flex flex-col gap-3">
          <div
            className="grid gap-2"
            style={{ gridTemplateColumns: `repeat(${grid.cols}, minmax(0, 1fr))` }}
          >
            {cellsInReadingOrder.map(({ index }) => {
              const region = regions[index]
              const isSelected = selected.includes(index)
              return (
                <div
                  key={`${region.row}-${region.col}`}
                  style={{ gridRow: `span ${region.row_span}`, gridColumn: `span ${region.col_span}` }}
                  className={`flex flex-col gap-1 rounded-md border p-2 ${
                    isSelected ? 'border-accent bg-accent-muted/40' : 'border-border bg-surface-2'
                  }`}
                >
                  <button
                    type="button"
                    onClick={() => toggleSelected(index)}
                    aria-pressed={isSelected}
                    className="text-left text-[10px] text-text-muted"
                  >
                    {region.row + 1}行{region.col + 1}列
                    {(region.row_span > 1 || region.col_span > 1) && ` (${region.row_span}×${region.col_span})`}
                  </button>
                  <select
                    value={region.content}
                    onChange={(event) => setRegions(setRegionContent(regions, index, event.target.value as MultiviewCell))}
                    className="w-full rounded border border-border bg-surface-1 px-1 py-1 text-xs text-text-primary"
                  >
                    {FIXED_CELL_OPTIONS.map((option) => (
                      <option key={option} value={option}>
                        {option}
                      </option>
                    ))}
                    {sources
                      .filter((source) => source.id !== null)
                      .map((source) => (
                        <option key={source.id} value={`SRC:${source.id}`}>
                          {describeCell(`SRC:${source.id}`)}
                        </option>
                      ))}
                  </select>
                </div>
              )
            })}
          </div>

          <Field label={`選択中: ${selected.length}領域`}>
            <span className="flex gap-2">
              <button
                type="button"
                disabled={!canMergeRegions(regions, selected)}
                onClick={handleMerge}
                className={secondaryButtonClass}
              >
                結合
              </button>
              <button
                type="button"
                disabled={selected.length !== 1}
                onClick={handleSplit}
                className={secondaryButtonClass}
              >
                分割
              </button>
              <button type="button" onClick={() => setSelected([])} className={secondaryButtonClass}>
                選択解除
              </button>
            </span>
          </Field>

          {errors.length > 0 && (
            <ul className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              {errors.slice(0, 5).map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          )}

          <button
            type="button"
            disabled={busy || errors.length > 0}
            onClick={() => void handleApply()}
            className={`self-start ${primaryButtonClass}`}
          >
            マルチビューを適用
          </button>
        </div>
      )}
    </Section>
  )
}
