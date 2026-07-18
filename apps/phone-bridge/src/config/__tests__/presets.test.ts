import { beforeEach, describe, expect, it } from 'vitest'
import type { PipSettings } from '../../protocol/types'
import { LocalStoragePresetStore, type ScenePreset } from '../presets'

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

beforeEach(() => {
  window.localStorage.clear()
})

describe('LocalStoragePresetStore', () => {
  it('round-trips a saved preset through load()', () => {
    const store = new LocalStoragePresetStore()
    const preset: ScenePreset = { name: 'wide-shot', channels: { 1: pip, 2: { ...pip, x_position: 500 } } }

    store.save(preset)

    expect(store.load('wide-shot')).toEqual(preset)
  })

  it('lists saved preset names', () => {
    const store = new LocalStoragePresetStore()
    store.save({ name: 'b', channels: { 1: pip } })
    store.save({ name: 'a', channels: { 1: pip } })

    expect(store.list()).toEqual(['a', 'b'])
  })

  it('returns null for a preset that was never saved', () => {
    const store = new LocalStoragePresetStore()
    expect(store.load('missing')).toBeNull()
  })

  it('removes a preset', () => {
    const store = new LocalStoragePresetStore()
    store.save({ name: 'wide-shot', channels: { 1: pip } })

    store.remove('wide-shot')

    expect(store.load('wide-shot')).toBeNull()
    expect(store.list()).toEqual([])
  })

  it('recovers gracefully from corrupted storage contents', () => {
    window.localStorage.setItem('phone-bridge:scene-presets', 'not json')
    const store = new LocalStoragePresetStore()

    expect(store.list()).toEqual([])
    store.save({ name: 'wide-shot', channels: { 1: pip } })
    expect(store.load('wide-shot')?.name).toBe('wide-shot')
  })
})
