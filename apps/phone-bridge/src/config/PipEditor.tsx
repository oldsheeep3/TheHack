import { useRef } from 'react'
import type { JSX, PointerEvent as ReactPointerEvent } from 'react'
import type { CropRect, PipSettings } from '../protocol/types'
import {
  DEFAULT_FRAME_SIZE,
  applyPreviewDrag,
  applyResize,
  clampCropRect,
  clampPipSettings,
  type FrameSize,
} from './layout'

const EMPTY_CROP: CropRect = { left: 0, top: 0, right: 0, bottom: 0 }

interface PipEditorProps {
  pip: PipSettings
  frame?: FrameSize
  onChange: (pip: PipSettings) => void
}

export function PipEditor({ pip, frame = DEFAULT_FRAME_SIZE, onChange }: PipEditorProps): JSX.Element {
  const previewRef = useRef<HTMLDivElement | null>(null)
  const dragPointerId = useRef<number | null>(null)
  const lastPointer = useRef<{ x: number; y: number } | null>(null)

  const handlePointerDown = (event: ReactPointerEvent<HTMLDivElement>): void => {
    dragPointerId.current = event.pointerId
    lastPointer.current = { x: event.clientX, y: event.clientY }
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  const handlePointerMove = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (dragPointerId.current !== event.pointerId || !lastPointer.current || !previewRef.current) return
    const rect = previewRef.current.getBoundingClientRect()
    const delta = { dx: event.clientX - lastPointer.current.x, dy: event.clientY - lastPointer.current.y }
    lastPointer.current = { x: event.clientX, y: event.clientY }
    onChange(applyPreviewDrag(pip, delta, { width: rect.width, height: rect.height }, frame))
  }

  const handlePointerUp = (event: ReactPointerEvent<HTMLDivElement>): void => {
    if (dragPointerId.current === event.pointerId) {
      dragPointerId.current = null
      lastPointer.current = null
    }
  }

  const updateField = (patch: Partial<PipSettings>): void => {
    onChange(clampPipSettings({ ...pip, ...patch }, frame))
  }

  const updateCrop = (patch: Partial<CropRect>): void => {
    const crop = clampCropRect({ ...(pip.crop ?? EMPTY_CROP), ...patch }, pip.width, pip.height)
    updateField({ crop })
  }

  const leftPercent = (pip.x_position / frame.width) * 100
  const topPercent = (pip.y_position / frame.height) * 100
  const widthPercent = (pip.width / frame.width) * 100
  const heightPercent = (pip.height / frame.height) * 100

  return (
    <div className="flex flex-col gap-4">
      <div
        ref={previewRef}
        className="relative aspect-video w-full overflow-hidden rounded-lg border border-border bg-surface-2"
      >
        <div
          role="button"
          tabIndex={0}
          aria-label="PiPレイアウト（ドラッグで移動）"
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          onPointerCancel={handlePointerUp}
          className="absolute touch-none cursor-grab rounded border-2 border-accent bg-accent-muted active:cursor-grabbing"
          style={{
            left: `${leftPercent}%`,
            top: `${topPercent}%`,
            width: `${widthPercent}%`,
            height: `${heightPercent}%`,
            opacity: pip.enabled ? pip.opacity : 0.25,
          }}
        />
      </div>

      <label className="flex items-center justify-between gap-3 text-base text-text-primary">
        表示 (enabled)
        <input
          type="checkbox"
          checked={pip.enabled}
          onChange={(event) => updateField({ enabled: event.target.checked })}
          className="h-6 w-6 accent-accent"
        />
      </label>

      <SliderField
        label="不透明度 (opacity)"
        value={pip.opacity}
        min={0}
        max={1}
        step={0.01}
        onChange={(value) => updateField({ opacity: value })}
      />
      <SliderField
        label="幅 (width)"
        value={pip.width}
        min={16}
        max={frame.width}
        step={10}
        onChange={(value) => onChange(applyResize(pip, { width: value, height: pip.height }, frame))}
      />
      <SliderField
        label="高さ (height)"
        value={pip.height}
        min={16}
        max={frame.height}
        step={10}
        onChange={(value) => onChange(applyResize(pip, { width: pip.width, height: value }, frame))}
      />
      <SliderField
        label="Zオーダー (z_order)"
        value={pip.z_order ?? 0}
        min={0}
        max={10}
        step={1}
        onChange={(value) => updateField({ z_order: value })}
      />

      <fieldset className="flex flex-col gap-3 rounded-lg border border-border p-3">
        <legend className="px-1 text-sm font-medium text-text-primary">クロップ (crop)</legend>
        <SliderField
          label="左 (left)"
          value={pip.crop?.left ?? 0}
          min={0}
          max={pip.width}
          step={1}
          onChange={(value) => updateCrop({ left: value })}
        />
        <SliderField
          label="上 (top)"
          value={pip.crop?.top ?? 0}
          min={0}
          max={pip.height}
          step={1}
          onChange={(value) => updateCrop({ top: value })}
        />
        <SliderField
          label="右 (right)"
          value={pip.crop?.right ?? 0}
          min={0}
          max={pip.width}
          step={1}
          onChange={(value) => updateCrop({ right: value })}
        />
        <SliderField
          label="下 (bottom)"
          value={pip.crop?.bottom ?? 0}
          min={0}
          max={pip.height}
          step={1}
          onChange={(value) => updateCrop({ bottom: value })}
        />
      </fieldset>
    </div>
  )
}

interface SliderFieldProps {
  label: string
  value: number
  min: number
  max: number
  step: number
  onChange: (value: number) => void
}

function SliderField({ label, value, min, max, step, onChange }: SliderFieldProps): JSX.Element {
  return (
    <label className="flex flex-col gap-1 text-sm text-text-primary">
      <span className="flex justify-between">
        <span>{label}</span>
        <span className="text-text-muted">{Math.round(value * 100) / 100}</span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="h-6 w-full accent-accent"
      />
    </label>
  )
}
