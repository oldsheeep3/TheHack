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
}

/** Shared card container used by every settings surface. */
export function Section({ title, children, actions }: SectionProps): JSX.Element {
  return (
    <div className="rounded-lg border border-border bg-surface-1 p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <p className="text-sm font-medium text-text-primary">{title}</p>
        {actions}
      </div>
      {children}
    </div>
  )
}
