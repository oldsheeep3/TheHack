import { useState } from 'react'

/** Creates a value once and keeps the same reference across re-renders (e.g. a client instance or a debounced fn). */
export function useStableInstance<T>(create: () => T): T {
  return useState(create)[0]
}
