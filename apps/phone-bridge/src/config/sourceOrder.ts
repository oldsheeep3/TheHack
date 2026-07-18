/**
 * Client-side display order for the source list (§2.2: "OBS的な並べ替え").
 * The PC side has no notion of source order, so the chosen order is kept in
 * `localStorage`, keyed by source id. Ordering/merging is pure and testable;
 * only load/save touch storage.
 */
const STORAGE_KEY = 'phone-bridge:source-order'

export interface OrderableSource {
  id: string | null
}

/** Sorts `sources` by `order` (ids not in `order` are appended, in their original relative order). */
export function sortSourcesByOrder<T extends OrderableSource>(sources: readonly T[], order: readonly string[]): T[] {
  const rank = new Map(order.map((id, index) => [id, index]))
  const ranked: { item: T; rank: number; originalIndex: number }[] = sources.map((item, originalIndex) => ({
    item,
    rank: item.id !== null && rank.has(item.id) ? (rank.get(item.id) as number) : order.length + originalIndex,
    originalIndex,
  }))
  ranked.sort((a, b) => a.rank - b.rank || a.originalIndex - b.originalIndex)
  return ranked.map((entry) => entry.item)
}

/** Returns a new order with `id` moved one slot toward the front (`up`) or back (`down`). */
export function moveIdInOrder(order: readonly string[], id: string, direction: 'up' | 'down'): string[] {
  const index = order.indexOf(id)
  if (index === -1) return order.slice()
  const targetIndex = direction === 'up' ? index - 1 : index + 1
  if (targetIndex < 0 || targetIndex >= order.length) return order.slice()
  const next = order.slice()
  ;[next[index], next[targetIndex]] = [next[targetIndex], next[index]]
  return next
}

/** Merges freshly seen ids into `order`, appending any not already present, preserving prior order otherwise. */
export function mergeOrder(order: readonly string[], seenIds: readonly string[]): string[] {
  const next = order.slice()
  for (const id of seenIds) {
    if (!next.includes(id)) next.push(id)
  }
  return next
}

export function loadSourceOrder(storage: Storage = window.localStorage): string[] {
  const raw = storage.getItem(STORAGE_KEY)
  if (!raw) return []
  try {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) && parsed.every((entry) => typeof entry === 'string') ? parsed : []
  } catch {
    return []
  }
}

export function saveSourceOrder(order: readonly string[], storage: Storage = window.localStorage): void {
  storage.setItem(STORAGE_KEY, JSON.stringify(order))
}
