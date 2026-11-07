import { AlertCircle, AlertTriangle, CheckCircle2, Info, Inbox } from 'lucide-react'
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { useId } from 'react'
import { ApiError } from '../lib/api'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'
const variants: Record<Variant, string> = {
  primary: 'bg-brand text-white shadow-sm hover:bg-brand-dark disabled:bg-brand/45',
  secondary: 'bg-card text-ink border border-line-strong shadow-sm hover:bg-paper disabled:text-ink-soft',
  danger: 'bg-card text-tone-danger border border-tone-danger/40 hover:bg-tone-danger-bg',
  ghost: 'text-ink-soft hover:text-ink hover:bg-ink/5',
}

/** 44px minimum touch target; disabled while busy so a slow network can't cause double submits. */
export function Button({ variant = 'primary', busy, className = '', children, disabled, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      {...rest}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 py-2.5 text-sm font-semibold transition-colors duration-150 disabled:cursor-not-allowed ${variants[variant]} ${className}`}
    >
      {busy && <Spinner small />}
      {children}
    </button>
  )
}

/** `flush` drops the inner padding for cards that hold full-width lists or tables. */
export function Card({ children, className = '', flush }: { children: ReactNode; className?: string; flush?: boolean }) {
  return <div className={`rounded-[var(--radius-card)] border border-line bg-card shadow-[var(--shadow-card)] ${flush ? 'overflow-hidden' : 'p-4 sm:p-5'} ${className}`}>{children}</div>
}

/** Section heading inside a card: title plus optional description and actions. */
export function CardHeader({ title, description, actions }: { title: ReactNode; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-base font-semibold tracking-tight">{title}</h2>
        {description && <p className="mt-0.5 text-sm text-ink-soft">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

/** Visible label always (never placeholder-only), hint and error tied to the input for screen readers. */
export function Field({ label, hint, error, children }: { label: string; hint?: string; error?: string[]; children: (id: string, describedBy?: string) => ReactNode }) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errId = error?.length ? `${id}-err` : undefined
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-ink">{label}</label>
      {children(id, [hintId, errId].filter(Boolean).join(' ') || undefined)}
      {hint && <p id={hintId} className="text-xs text-ink-soft">{hint}</p>}
      {error?.map((e) => <p key={e} id={errId} className="flex items-center gap-1 text-xs font-medium text-tone-danger"><AlertCircle aria-hidden className="size-3.5 shrink-0" />{e}</p>)}
    </div>
  )
}

const inputCls = 'block w-full min-h-11 rounded-lg border border-line-strong bg-card px-3 py-2 text-base text-ink shadow-sm placeholder:text-ink-soft/60 transition-colors focus:border-brand focus:outline-none focus:ring-2 focus:ring-brand/15 aria-[invalid=true]:border-tone-danger'

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${inputCls} ${props.className ?? ''}`} />
}

export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={`${inputCls} ${props.className ?? ''}`} />
}

export function Spinner({ small }: { small?: boolean }) {
  return <span role="status" aria-label="Loading" className={`inline-block animate-spin rounded-full border-2 border-current border-r-transparent ${small ? 'size-4' : 'size-6'}`} />
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <div className="flex items-center justify-center gap-3 py-10 text-ink-soft"><Spinner /> <span>{label}</span></div>
}

type Tone = 'info' | 'warn' | 'danger' | 'neutral'
const alertTones: Record<Tone, string> = {
  info: 'bg-tone-info-bg text-tone-info border-tone-info/25',
  warn: 'bg-tone-warn-bg text-tone-warn border-tone-warn/25',
  danger: 'bg-tone-danger-bg text-tone-danger border-tone-danger/25',
  neutral: 'bg-card text-ink-soft border-line',
}
const alertIcons = { info: Info, warn: AlertTriangle, danger: AlertCircle, neutral: Info } as const

/** Icon + text, so the message never depends on colour alone (brief §5.6). */
export function Alert({ tone = 'info', title, children }: { tone?: Tone; title?: string; children?: ReactNode }) {
  const Icon = alertIcons[tone]
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={`flex gap-3 rounded-lg border px-4 py-3 text-sm ${alertTones[tone]}`}>
      <Icon aria-hidden className="mt-0.5 size-4 shrink-0" />
      <div className="min-w-0">
        {title && <p className={`font-semibold ${tone === 'neutral' ? 'text-ink' : ''}`}>{title}</p>}
        {children && <div className={title ? 'mt-1' : ''}>{children}</div>}
      </div>
    </div>
  )
}

export function SuccessNote({ children }: { children: ReactNode }) {
  return <p role="status" className="flex items-center gap-1.5 text-sm font-medium text-tone-info"><CheckCircle2 aria-hidden className="size-4" />{children}</p>
}

/** Renders any thrown error as a readable alert, including server field errors. */
export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null
  if (error instanceof ApiError) {
    if (error.status === 404) return <Alert tone="neutral" title="Not found">This item does not exist or you do not have access to it.</Alert>
    return (
      <Alert tone="danger" title={error.message}>
        {error.details.length > 0 && <ul className="list-disc pl-5">{error.details.map((d) => <li key={d}>{d}</li>)}</ul>}
      </Alert>
    )
  }
  return <Alert tone="danger" title="Something went wrong." />
}

export function EmptyState({ title, children, action, icon: Icon = Inbox }: { title: string; children?: ReactNode; action?: ReactNode; icon?: typeof Inbox }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-dashed border-line-strong bg-card px-6 py-10 text-center">
      <span className="mx-auto mb-3 grid size-10 place-items-center rounded-full bg-paper text-ink-soft"><Icon aria-hidden className="size-5" /></span>
      <p className="font-semibold">{title}</p>
      {children && <div className="mx-auto mt-1 max-w-sm text-sm text-ink-soft">{children}</div>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

/** A labelled figure. Money and counts use tabular numerals so columns line up. */
export function Stat({ label, value, note }: { label: string; value: ReactNode; note?: string }) {
  return (
    <div className="rounded-lg border border-line bg-card px-3 py-3">
      <dt className="text-xs font-medium uppercase tracking-wide text-ink-soft">{label}</dt>
      <dd className="mt-1 text-lg font-semibold tabular-nums tracking-tight">{value}</dd>
      {note && <dd className="text-xs text-ink-soft">{note}</dd>}
    </div>
  )
}
