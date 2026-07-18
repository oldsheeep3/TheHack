import { describe, expect, it } from 'vitest'
import type { PipSettings } from '../../protocol/types'
import {
  applyPreviewDrag,
  applyResize,
  clampCropRect,
  clampPipSettings,
  previewDeltaToFrameDelta,
} from '../layout'

const basePip: PipSettings = {
  enabled: true,
  x_position: 100,
  y_position: 100,
  width: 480,
  height: 270,
  opacity: 1,
  z_order: 1,
  crop: null,
}

describe('clampPipSettings', () => {
  it('leaves an in-bounds PiP untouched', () => {
    expect(clampPipSettings(basePip)).toEqual(basePip)
  })

  it('clamps position so the PiP never exceeds the frame edges', () => {
    const result = clampPipSettings({ ...basePip, x_position: 3000, y_position: -500 })
    expect(result.x_position).toBe(1920 - basePip.width)
    expect(result.y_position).toBe(0)
  })

  it('clamps width/height to the frame size', () => {
    const result = clampPipSettings({ ...basePip, width: 5000, height: 5000 })
    expect(result.width).toBe(1920)
    expect(result.height).toBe(1080)
  })

  it('clamps opacity to [0, 1]', () => {
    expect(clampPipSettings({ ...basePip, opacity: 1.5 }).opacity).toBe(1)
    expect(clampPipSettings({ ...basePip, opacity: -0.5 }).opacity).toBe(0)
  })

  it('clamps a crop rect against the (already clamped) width/height', () => {
    const result = clampPipSettings({
      ...basePip,
      crop: { left: -10, top: 10, right: 10000, bottom: 10000 },
    })
    expect(result.crop).toEqual({ left: 0, top: 10, right: basePip.width, bottom: basePip.height - 10 })
  })
})

describe('clampCropRect', () => {
  it('keeps left+right and top+bottom within width/height', () => {
    expect(clampCropRect({ left: 300, top: 200, right: 300, bottom: 200 }, 480, 270)).toEqual({
      left: 300,
      top: 200,
      right: 180,
      bottom: 70,
    })
  })
})

describe('previewDeltaToFrameDelta', () => {
  it('scales a preview-space delta up to frame space', () => {
    const delta = previewDeltaToFrameDelta({ dx: 96, dy: 54 }, { width: 384, height: 216 })
    expect(delta).toEqual({ dx: 480, dy: 270 })
  })

  it('returns zero delta for a degenerate preview size', () => {
    expect(previewDeltaToFrameDelta({ dx: 10, dy: 10 }, { width: 0, height: 0 })).toEqual({ dx: 0, dy: 0 })
  })
})

describe('applyPreviewDrag', () => {
  it('moves the PiP by the scaled delta and clamps it in bounds', () => {
    const result = applyPreviewDrag(basePip, { dx: 96, dy: 54 }, { width: 384, height: 216 })
    expect(result.x_position).toBe(580)
    expect(result.y_position).toBe(370)
  })

  it('clamps when the drag would push the PiP off the right/bottom edge', () => {
    const result = applyPreviewDrag(basePip, { dx: 10_000, dy: 10_000 }, { width: 384, height: 216 })
    expect(result.x_position).toBe(1920 - basePip.width)
    expect(result.y_position).toBe(1080 - basePip.height)
  })
})

describe('applyResize', () => {
  it('applies a new size and clamps position if it no longer fits', () => {
    const result = applyResize({ ...basePip, x_position: 1800 }, { width: 800, height: 270 })
    expect(result.width).toBe(800)
    expect(result.x_position).toBe(1920 - 800)
  })
})
