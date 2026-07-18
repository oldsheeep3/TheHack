/**
 * Scene preset storage: a saved snapshot of every channel's `PipSettings`.
 * Stored in `localStorage` today; kept behind `PresetStore` so a future
 * PC-side presets API can be swapped in without touching callers.
 */
import type { PipSettings } from '../protocol/types'

export interface ScenePreset {
  name: string
  channels: Record<number, PipSettings>
}

export interface PresetStore {
  list(): string[]
  load(name: string): ScenePreset | null
  save(preset: ScenePreset): void
  remove(name: string): void
}

const STORAGE_KEY = 'phone-bridge:scene-presets'

export class LocalStoragePresetStore implements PresetStore {
  private readonly storage: Storage

  constructor(storage: Storage = window.localStorage) {
    this.storage = storage
  }

  list(): string[] {
    return Object.keys(this.readAll()).sort()
  }

  load(name: string): ScenePreset | null {
    return this.readAll()[name] ?? null
  }

  save(preset: ScenePreset): void {
    const all = this.readAll()
    all[preset.name] = preset
    this.writeAll(all)
  }

  remove(name: string): void {
    const all = this.readAll()
    delete all[name]
    this.writeAll(all)
  }

  private readAll(): Record<string, ScenePreset> {
    const raw = this.storage.getItem(STORAGE_KEY)
    if (!raw) return {}
    try {
      const parsed: unknown = JSON.parse(raw)
      return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, ScenePreset>) : {}
    } catch {
      return {}
    }
  }

  private writeAll(all: Record<string, ScenePreset>): void {
    this.storage.setItem(STORAGE_KEY, JSON.stringify(all))
  }
}
