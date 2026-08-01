import type { JSX, ReactNode } from 'react'

export type BadgeTone = 'danger' | 'success' | 'accent' | 'muted'

const TONE_CLASS: Record<BadgeTone, string> = {
  danger: 'bg-danger text-white',
  success: 'bg-success text-black',
  accent: 'bg-accent-muted text-text-primary',
  muted: 'bg-surface-2 text-text-muted',
}

interface BadgeProps {
  label: string
  tone: BadgeTone
}

/** Small color+text status chip (§5: 状態はアイコン＋色で即判別). */
export function Badge({ label, tone }: BadgeProps): JSX.Element {
  return <span className={`rounded px-2 py-0.5 text-xs font-semibold ${TONE_CLASS[tone]}`}>{label}</span>
}

interface SectionProps {
  title: string
  children: ReactNode
  actions?: ReactNode
  /** One line under the title explaining what the PC does with these settings. */
  hint?: string
}

/** Shared card container used by every settings surface. */
export function Section({ title, children, actions, hint }: SectionProps): JSX.Element {
  return (
    <div className="rounded-lg border border-border bg-surface-1 p-4">
      <div className="mb-3 flex items-start justify-between gap-2">
        <div>
          <p className="text-sm font-medium text-text-primary">{title}</p>
          {hint && <p className="mt-0.5 text-xs text-text-muted">{hint}</p>}
        </div>
        {actions}
      </div>
      {children}
    </div>
  )
}

/** Shared control classes, so every panel's inputs line up without re-typing the Tailwind soup. */
export const inputClass = 'rounded-md border border-border bg-surface-2 px-3 py-2 text-base text-text-primary'

export const selectClass = 'rounded-md border border-border bg-surface-1 px-2 py-1 text-sm text-text-primary'

export const primaryButtonClass =
  'rounded-md bg-accent px-4 py-2 text-sm font-medium text-white disabled:opacity-40'

export const secondaryButtonClass =
  'rounded-md bg-surface-2 px-3 py-2 text-sm font-medium text-text-primary disabled:opacity-40'

export const dangerButtonClass =
  'rounded-md bg-danger/20 px-3 py-2 text-sm font-medium text-danger disabled:opacity-40'

/** Label + control row, the layout most settings fields in this app use. */
export function Field({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return (
    <label className="flex items-center justify-between gap-2 text-sm text-text-primary">
      {label}
      {children}
    </label>
  )
}

/** Label above a full-width control, for anything a phone-width row cannot hold. */
export function StackedField({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return (
    <label className="flex flex-col gap-1 text-sm text-text-primary">
      {label}
      {children}
    </label>
  )
}
