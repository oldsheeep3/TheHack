import { beforeEach, describe, expect, it } from 'vitest'
import type { PipSettings } from '../../protocol/types'
import { DEFAULT_GRID, createGridRegions } from '../multiview'
import { LocalStoragePresetStore, emptyScenePreset, type ScenePreset } from '../presets'

const pip: PipSettings = {
  enabled: true,
  x_position: 10,
  y_position: 20,
  width: 480,
  height: 270,
  opacity: 1,
  z_order: 1,
  crop: null,
}

function presetWithLayer(name: string): ScenePreset {
  const preset = emptyScenePreset(name)
  preset.programs.PGM1 = [{ source_id: 'src-1', pip }]
  return preset
}

beforeEach(() => {
  window.localStorage.clear()
})

describe('emptyScenePreset', () => {
  it('starts with empty layer stacks and a fully-empty 4x4 multiview', () => {
    const preset = emptyScenePreset('blank')
    expect(preset.programs).toEqual({ PGM1: [], PGM2: [] })
    expect(preset.multiviewGrid).toEqual(DEFAULT_GRID)
    expect(preset.multiviewRegions).toEqual(createGridRegions(DEFAULT_GRID))
  })
})

describe('LocalStoragePresetStore', () => {
  it('round-trips a saved preset through load()', () => {
    const store = new LocalStoragePresetStore()
    const preset = presetWithLayer('wide-shot')

    store.save(preset)

    expect(store.load('wide-shot')).toEqual(preset)
  })

  it('lists saved preset names', () => {
    const store = new LocalStoragePresetStore()
    store.save(emptyScenePreset('b'))
    store.save(emptyScenePreset('a'))

    expect(store.list()).toEqual(['a', 'b'])
  })

  it('returns null for a preset that was never saved', () => {
    const store = new LocalStoragePresetStore()
    expect(store.load('missing')).toBeNull()
  })

  it('removes a preset', () => {
    const store = new LocalStoragePresetStore()
    store.save(emptyScenePreset('wide-shot'))

    store.remove('wide-shot')

    expect(store.load('wide-shot')).toBeNull()
    expect(store.list()).toEqual([])
  })

  it('recovers gracefully from corrupted storage contents', () => {
    window.localStorage.setItem('phone-bridge:scene-presets-v3', 'not json')
    const store = new LocalStoragePresetStore()

    expect(store.list()).toEqual([])
    store.save(emptyScenePreset('wide-shot'))
    expect(store.load('wide-shot')?.name).toBe('wide-shot')
  })
})
