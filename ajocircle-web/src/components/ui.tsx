import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { useId } from 'react'
import { ApiError } from '../lib/api'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'
const variants: Record<Variant, string> = {
  primary: 'bg-brand text-white hover:bg-brand-dark disabled:bg-brand/50',
  secondary: 'bg-card text-ink border border-line hover:bg-paper disabled:text-ink-soft',
  danger: 'bg-card text-tone-danger border border-tone-danger/40 hover:bg-tone-danger-bg',
  ghost: 'text-ink-soft hover:text-ink hover:bg-ink/5',
}

export function Button({ variant = 'primary', busy, className = '', children, disabled, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      {...rest}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold transition-colors disabled:cursor-not-allowed ${variants[variant]} ${className}`}
    >
      {busy && <Spinner small />}
      {children}
    </button>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-2xl border border-line bg-card p-4 sm:p-5 ${className}`}>{children}</div>
}

export function Field({ label, hint, error, children }: { label: string; hint?: string; error?: string[]; children: (id: string, describedBy?: string) => ReactNode }) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errId = error?.length ? `${id}-err` : undefined
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-semibold">{label}</label>
      {children(id, [hintId, errId].filter(Boolean).join(' ') || undefined)}
      {hint && <p id={hintId} className="text-xs text-ink-soft">{hint}</p>}
      {error?.map((e) => <p key={e} id={errId} className="text-xs font-medium text-tone-danger">{e}</p>)}
    </div>
  )
}

const inputCls = 'block w-full min-h-11 rounded-xl border border-line bg-card px-3 py-2 text-base text-ink placeholder:text-ink-soft/60 focus:border-brand focus:outline-none aria-[invalid=true]:border-tone-danger'

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
  return <div className="flex items-center gap-3 py-10 text-ink-soft justify-center"><Spinner /> <span>{label}</span></div>
}

type Tone = 'info' | 'warn' | 'danger' | 'neutral'
const alertTones: Record<Tone, string> = {
  info: 'bg-tone-info-bg text-tone-info border-tone-info/30',
  warn: 'bg-tone-warn-bg text-tone-warn border-tone-warn/30',
  danger: 'bg-tone-danger-bg text-tone-danger border-tone-danger/30',
  neutral: 'bg-tone-neutral-bg text-tone-neutral border-tone-neutral/20',
}

export function Alert({ tone = 'info', title, children }: { tone?: Tone; title?: string; children?: ReactNode }) {
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={`rounded-xl border px-4 py-3 text-sm ${alertTones[tone]}`}>
      {title && <p className="font-semibold">{title}</p>}
      {children && <div className={title ? 'mt-1' : ''}>{children}</div>}
    </div>
  )
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

export function EmptyState({ title, children, action }: { title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed border-line bg-card/60 px-6 py-10 text-center">
      <p className="font-semibold">{title}</p>
      {children && <div className="mx-auto mt-1 max-w-sm text-sm text-ink-soft">{children}</div>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function Stat({ label, value, note }: { label: string; value: ReactNode; note?: string }) {
  return (
    <div className="rounded-xl bg-paper px-3 py-3">
      <dt className="text-xs text-ink-soft">{label}</dt>
      <dd className="mt-0.5 text-lg font-bold tabular-nums">{value}</dd>
      {note && <dd className="text-xs text-ink-soft">{note}</dd>}
    </div>
  )
}
