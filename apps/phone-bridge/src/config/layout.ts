/**
 * Pure PiP layout math (§4.2 `PipSettings`): drag/resize deltas in preview
 * pixel space → frame-space `PipSettings`, always boundary-clamped. Kept
 * free of React/DOM so it can be unit tested without a rendered component.
 */
import type { CropRect, PipSettings } from '../protocol/types'

export interface FrameSize {
  width: number
  height: number
}

/** Virtual composited output frame the PiP coordinates are expressed in. */
export const DEFAULT_FRAME_SIZE: FrameSize = { width: 1920, height: 1080 }

export interface DragDelta {
  dx: number
  dy: number
}

export interface PreviewSize {
  width: number
  height: number
}

function clampNumber(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), Math.max(min, max))
}

export function clampCropRect(crop: CropRect, width: number, height: number): CropRect {
  const left = clampNumber(crop.left, 0, width)
  const top = clampNumber(crop.top, 0, height)
  const right = clampNumber(crop.right, 0, width - left)
  const bottom = clampNumber(crop.bottom, 0, height - top)
  return { left, top, right, bottom }
}

/** Clamps every field of a `PipSettings` so it stays within `frame` bounds. */
export function clampPipSettings(
  pip: PipSettings,
  frame: FrameSize = DEFAULT_FRAME_SIZE,
): PipSettings {
  const width = clampNumber(pip.width, 1, frame.width)
  const height = clampNumber(pip.height, 1, frame.height)
  const x_position = clampNumber(pip.x_position, 0, frame.width - width)
  const y_position = clampNumber(pip.y_position, 0, frame.height - height)
  const opacity = clampNumber(pip.opacity, 0, 1)
  const crop = pip.crop ? clampCropRect(pip.crop, width, height) : (pip.crop ?? null)

  return { ...pip, x_position, y_position, width, height, opacity, crop }
}

/** Converts a pointer-drag delta measured in on-screen preview pixels to frame-space pixels. */
export function previewDeltaToFrameDelta(
  delta: DragDelta,
  preview: PreviewSize,
  frame: FrameSize = DEFAULT_FRAME_SIZE,
): DragDelta {
  if (preview.width <= 0 || preview.height <= 0) return { dx: 0, dy: 0 }
  return {
    dx: (delta.dx / preview.width) * frame.width,
    dy: (delta.dy / preview.height) * frame.height,
  }
}

/** Applies a preview-space drag delta to a PiP's position, clamped to `frame`. */
export function applyPreviewDrag(
  pip: PipSettings,
  delta: DragDelta,
  preview: PreviewSize,
  frame: FrameSize = DEFAULT_FRAME_SIZE,
): PipSettings {
  const frameDelta = previewDeltaToFrameDelta(delta, preview, frame)
  return clampPipSettings(
    { ...pip, x_position: pip.x_position + frameDelta.dx, y_position: pip.y_position + frameDelta.dy },
    frame,
  )
}

/** Applies a new size (frame-space) to a PiP, clamped to `frame`. */
export function applyResize(
  pip: PipSettings,
  size: { width: number; height: number },
  frame: FrameSize = DEFAULT_FRAME_SIZE,
): PipSettings {
  return clampPipSettings({ ...pip, width: size.width, height: size.height }, frame)
}
