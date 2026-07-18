import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import type { MultiviewCell, SourceInfo, TallyState } from '../protocol/types'
import { parseSourceCell, setMultiviewCell, sourceCell } from './multiview'
import { Section } from './ui'

const FIXED_CELL_OPTIONS: MultiviewCell[] = ['EMPTY', 'PGM1', 'PGM2', 'PVW1', 'PVW2']

interface MultiviewPanelProps {
  apiClient: ApiClient
  sources: SourceInfo[]
  tally: TallyState | null
  cells: MultiviewCell[]
  onCellsChange: (cells: MultiviewCell[]) => void
  onError: (message: string) => void
}

export function MultiviewPanel({
  apiClient,
  sources,
  tally,
  cells,
  onCellsChange,
  onError,
}: MultiviewPanelProps): JSX.Element {
  const send = (next: MultiviewCell[]): void => {
    apiClient
      .setMultiview({ cells: next })
      .catch((error: unknown) => onError(error instanceof Error ? error.message : String(error)))
  }

  const handleCellChange = (index: number, value: MultiviewCell): void => {
    const next = setMultiviewCell(cells, index, value)
    onCellsChange(next)
    send(next)
  }

  const describeCell = (cell: MultiviewCell): string => {
    const sourceId = parseSourceCell(cell)
    if (sourceId === null) return cell
    const source = sources.find((candidate) => candidate.id === sourceId)
    return source ? `SRC: ${source.name}` : cell
  }

  const isLiveCell = (cell: MultiviewCell): boolean => {
    if (!tally) return false
    if (cell === 'PGM1') return tally.active_pgm1.length > 0
    if (cell === 'PGM2') return tally.active_pgm2.length > 0
    if (cell === 'PVW1') return tally.active_pvw1.length > 0
    if (cell === 'PVW2') return tally.active_pvw2.length > 0
    return false
  }

  return (
    <Section title="4x4 マルチビュー割当">
      <div className="grid grid-cols-4 gap-2">
        {cells.map((cell, index) => (
          <div
            key={index}
            className={`flex flex-col gap-1 rounded-md border p-2 ${
              isLiveCell(cell) ? 'border-accent bg-accent-muted/40' : 'border-border bg-surface-2'
            }`}
          >
            <span className="text-[10px] text-text-muted">セル {index + 1}</span>
            <select
              value={cell}
              onChange={(event) => handleCellChange(index, event.target.value as MultiviewCell)}
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
                  <option key={source.id} value={sourceCell(source.id as string)}>
                    {describeCell(sourceCell(source.id as string))}
                  </option>
                ))}
            </select>
          </div>
        ))}
      </div>
    </Section>
  )
}
