import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, Layers, Users } from 'lucide-react'
import { brand } from '../../app/brand'
import { usePageMeta } from '../../app/usePageMeta'
import { Alert, Button, Card, ErrorAlert, Field, Loading, PasswordInput, TextInput } from '../../components/ui'
import { meKey, useMe } from '../../hooks/useMe'
import { api, ApiError } from '../../lib/api'
import type { AccountRole, Me } from '../../types/api'
import { homeFor } from '../../lib/workspace'

const onboardingFor = (me: Me) => (me.role === 'Organizer' ? '/onboarding/organizer' : '/onboarding/member')

/** `?next=` carried through sign-up and log-in, encoded for a URL (left off when there is none). */
const withNext = (path: string, next: string) => (next ? `${path}?next=${encodeURIComponent(next)}` : path)

/** Only allow in-app relative redirects (no open redirect). */
function safeNext(raw: string | null, fallback = '') {
  return raw && raw.startsWith('/') && !raw.startsWith('//') ? raw : fallback
}

function AuthShell({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  usePageMeta(title)
  return (
    <div className="grid min-h-[calc(100dvh-4rem)] lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      {/* Brand panel — tablet landscape and up only; phones get a single focused column. */}
      <aside className="relative hidden overflow-hidden bg-brand px-10 py-14 text-white lg:flex lg:flex-col lg:justify-between">
        <div aria-hidden className="absolute -right-24 -top-24 size-80 rounded-full border-[28px] border-white/5" />
        <div aria-hidden className="absolute -bottom-32 -left-16 size-96 rounded-full border-[36px] border-mint/10" />
        <div className="relative max-w-md">
          <p className="font-mono text-[11px] uppercase tracking-[0.08em] text-lime">{brand.name}</p>
          <p className="mt-5 text-4xl font-light leading-[1.05] tracking-[-0.04em]">{brand.tagline}</p>
          <ul className="mt-8 space-y-3 text-sm text-white/80">
            {['Every member sees the same rules and dates.', 'Records are permanent; corrections are linked, never overwritten.', 'Tracking only — your money stays with your group.'].map((t) => (
              <li key={t} className="flex gap-2.5"><CheckCircle2 aria-hidden className="mt-0.5 size-4 shrink-0 text-mint" />{t}</li>
            ))}
          </ul>
        </div>
        <p className="relative text-xs text-white/55">{brand.name} does not hold, collect or transfer money.</p>
      </aside>
      <div className="flex items-start justify-center bg-paper px-4 py-10 sm:items-center sm:py-14">
        <div className="w-full max-w-md">
          <h1 className="text-[32px] font-light leading-[1.1] tracking-[-0.035em]">{title}</h1>
          {subtitle && <p className="mt-3 text-[15px] text-ink-soft">{subtitle}</p>}
          <Card className="mt-6 sm:p-6">{children}</Card>
        </div>
      </div>
    </div>
  )
}

export function RegisterPage() {
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const qc = useQueryClient()
  const navigate = useNavigate()
  const preset = params.get('role')
  const [step, setStep] = useState<1 | 2>(1)
  const [form, setForm] = useState({
    displayName: '', email: '', password: '', confirm: '', acceptTerms: false,
    role: (preset === 'organizer' ? 'Organizer' : preset === 'member' ? 'Member' : null) as AccountRole | null,
  })
  const [local, setLocal] = useState<Record<string, string[]>>({})
  const m = useMutation({
    mutationFn: () => api.post<Me>('/auth/register', { displayName: form.displayName, email: form.email, password: form.password, acceptTerms: form.acceptTerms, role: form.role }),
    onSuccess: (me) => {
      qc.setQueryData(meKey, me)
      navigate(withNext('/verify-account', next))
    },
    // A problem with the details sends the person back to step 1, where those fields are.
    onError: (e) => { if (e instanceof ApiError && ['displayName', 'email', 'password', 'acceptTerms'].some((k) => e.fieldErrors[k])) setStep(1) },
  })
  const server = m.error instanceof ApiError ? m.error.fieldErrors : {}
  const fe = (k: string) => local[k] ?? server[k]

  /** Step 1 is checked in the browser for quick feedback; the server checks everything again. */
  const toStep2 = (e: FormEvent) => {
    e.preventDefault()
    const errs: Record<string, string[]> = {}
    if (form.displayName.trim().length < 2) errs.displayName = ['Enter your name (at least 2 characters).']
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) errs.email = ['Enter a valid email address.']
    if (form.password.length < 10) errs.password = ['Use at least 10 characters.']
    if (form.confirm !== form.password) errs.confirm = ['The passwords don’t match.']
    if (!form.acceptTerms) errs.acceptTerms = ['You must accept the Terms of Service and Privacy Notice.']
    setLocal(errs)
    if (Object.keys(errs).length === 0) setStep(2)
  }

  return (
    <AuthShell title="Create your account" subtitle={`Step ${step} of 2 · ${step === 1 ? 'Your details' : `How you’ll use ${brand.name}`}`}>
      <ol className="mb-5 grid grid-cols-2 gap-2" aria-label="Sign-up progress">
        {[1, 2].map((n) => (
          <li key={n} aria-current={step === n ? 'step' : undefined} className={`h-1.5 rounded-full ${n <= step ? 'bg-teal' : 'bg-line'}`}>
            <span className="sr-only">Step {n}{n < step ? ' (done)' : ''}</span>
          </li>
        ))}
      </ol>
      {step === 1 ? (
        <form className="space-y-4" onSubmit={toStep2} noValidate>
          <Field label="Your name" hint="Shown to the people in your groups." error={fe('displayName')}>
            {(id, d) => <TextInput id={id} aria-describedby={d} aria-invalid={!!fe('displayName')} autoComplete="name" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} required />}
          </Field>
          <Field label="Email" error={fe('email')}>
            {(id, d) => <TextInput id={id} aria-describedby={d} aria-invalid={!!fe('email')} type="email" autoComplete="email" inputMode="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required />}
          </Field>
          <Field label="Password" hint="At least 10 characters. A short sentence is easy to remember." error={fe('password')}>
            {(id, d) => <PasswordInput id={id} aria-describedby={d} aria-invalid={!!fe('password')} autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />}
          </Field>
          <Field label="Confirm password" error={fe('confirm')}>
            {(id, d) => <PasswordInput id={id} aria-describedby={d} aria-invalid={!!fe('confirm')} autoComplete="new-password" value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} required />}
          </Field>
          <label className="flex items-start gap-3 text-sm">
            <input type="checkbox" className="mt-1 size-4 accent-teal" checked={form.acceptTerms} onChange={(e) => setForm({ ...form, acceptTerms: e.target.checked })} />
            <span>I accept the <Link to="/terms" className="font-semibold underline">Terms of Service</Link> and <Link to="/privacy" className="font-semibold underline">Privacy Notice</Link>.</span>
          </label>
          {fe('acceptTerms') && <p className="text-xs font-medium text-tone-danger">{fe('acceptTerms')![0]}</p>}
          <Button type="submit" className="w-full">Continue</Button>
        </form>
      ) : (
        <form className="space-y-4" onSubmit={(e: FormEvent) => { e.preventDefault(); m.mutate() }} noValidate>
          <fieldset>
            <legend className="mb-3 text-sm font-semibold">I’m joining as…</legend>
            <div className="space-y-3">
              {([
                ['Member', Users, 'A member', 'I save in a group someone else runs. I’ll join with an invite link from my organizer.'],
                ['Organizer', Layers, 'An organizer', 'I run ajo or savings groups, and can also join other people’s groups. Organizers complete a stronger identity check.'],
              ] as const).map(([value, Icon, title, text]) => (
                <label key={value} className={`flex cursor-pointer gap-3 rounded-lg border p-4 transition-colors ${form.role === value ? 'border-teal bg-teal-soft ring-1 ring-teal' : 'border-line-strong hover:bg-paper'}`}>
                  <input type="radio" name="role" value={value} checked={form.role === value} onChange={() => setForm({ ...form, role: value })} className="mt-1 accent-teal" />
                  <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-lg bg-card text-teal ring-1 ring-line"><Icon className="size-5" /></span>
                  <span><span className="block font-semibold">{title}</span><span className="text-sm text-ink-soft">{text}</span></span>
                </label>
              ))}
            </div>
          </fieldset>
          {server.role && <p className="text-xs font-medium text-tone-danger">{server.role[0]}</p>}
          <p className="text-xs text-ink-soft">You can’t switch this yourself later. A member becomes an organizer by passing the organizer identity check.</p>
          {m.error && !(m.error instanceof ApiError && m.error.status === 400) && <ErrorAlert error={m.error} />}
          <div className="flex flex-col-reverse gap-2 sm:flex-row">
            <Button type="button" variant="secondary" onClick={() => setStep(1)} className="sm:flex-1">Back</Button>
            <Button type="submit" busy={m.isPending} disabled={!form.role} className="sm:flex-[2]">Create account</Button>
          </div>
        </form>
      )}
      <p className="mt-4 text-center text-sm text-ink-soft">Already have an account? <Link to={withNext('/login', next)} className="font-semibold text-ink underline">Log in</Link></p>
    </AuthShell>
  )
}

export function LoginPage() {
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { data: me } = useMe()
  const [form, setForm] = useState({ email: '', password: '' })
  const m = useMutation({
    mutationFn: () => api.post<Me>('/auth/login', form),
    onSuccess: (me) => {
      qc.setQueryData(meKey, me)
      navigate(me.contactVerified ? next || homeFor(me) : withNext('/verify-account', next))
    },
  })
  if (me && !m.isSuccess) return <Navigate to={next || homeFor(me)} replace />

  return (
    <AuthShell title="Log in" subtitle="Welcome back. Log in to see your groups.">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); m.mutate() }}>
        <Field label="Email">
          {(id) => <TextInput id={id} type="email" autoComplete="email" inputMode="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required />}
        </Field>
        <Field label="Password">
          {(id) => <PasswordInput id={id} autoComplete="current-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />}
        </Field>
        <p className="-mt-2 text-right text-sm"><Link to="/forgot-password" className="font-semibold text-ink-soft underline hover:text-ink">Forgot your password?</Link></p>
        <ErrorAlert error={m.error} />
        <Button type="submit" busy={m.isPending} className="w-full">Log in</Button>
      </form>
      <p className="mt-4 text-center text-sm text-ink-soft">New here? <Link to={withNext('/register', next)} className="font-semibold text-ink underline">Create an account</Link></p>
    </AuthShell>
  )
}

export function VerifyPage() {
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { data: me, isLoading } = useMe()
  const [code, setCode] = useState('')
  const verify = useMutation({
    mutationFn: () => api.post<Me>('/auth/verify-contact', { code }),
    onSuccess: (me) => { qc.setQueryData(meKey, me); navigate(next || onboardingFor(me)) },
  })
  const resend = useMutation({ mutationFn: () => api.post('/auth/resend-verification') })

  if (isLoading) return <Loading />
  if (!me) return <Navigate to={`/login?next=${encodeURIComponent('/verify-account')}`} replace />
  if (me.contactVerified && !verify.isSuccess) return <Navigate to={next || homeFor(me)} replace />

  return (
    <AuthShell title="Verify your email" subtitle={`We sent a 6-digit code to ${me.email}. It expires in 15 minutes.`}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); verify.mutate() }}>
        <Field label="Verification code">
          {(id) => (
            <TextInput id={id} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" maxLength={6}
              className="text-center text-2xl tracking-[0.5em] tabular-nums" value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} required />
          )}
        </Field>
        <ErrorAlert error={verify.error} />
        {resend.isSuccess && <Alert tone="info">A new code is on its way.</Alert>}
        <Button type="submit" busy={verify.isPending} disabled={code.length !== 6} className="w-full">Verify</Button>
        <Button type="button" variant="ghost" busy={resend.isPending} onClick={() => resend.mutate()} className="w-full">Send a new code</Button>
      </form>
      <DevInboxHint />
    </AuthShell>
  )
}

/** Local development only: emails land in the Mailpit test inbox instead of real mailboxes. */
export function DevInboxHint() {
  if (!import.meta.env.DEV) return null
  return <p className="mt-3 text-center text-xs text-ink-soft">Developer: emails go to the local test inbox at <a className="underline" href="http://localhost:8025" target="_blank" rel="noreferrer">localhost:8025</a>.</p>
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const m = useMutation({ mutationFn: () => api.post('/auth/forgot-password', { email }) })
  return (
    <AuthShell title="Reset your password" subtitle="We’ll email you a link to choose a new one.">
      {m.isSuccess ? (
        <div className="space-y-3">
          <Alert tone="info" title="Check your email">If an account uses {email}, a reset link is on its way. It works once and expires in 30 minutes.</Alert>
          <p className="text-sm text-ink-soft">No email after a few minutes? Check spam, or try again later.</p>
          <Link to="/login" className="inline-block text-sm font-semibold underline">Back to sign in</Link>
        </div>
      ) : (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); m.mutate() }}>
          <Field label="Email">
            {(id) => <TextInput id={id} type="email" autoComplete="email" inputMode="email" value={email} onChange={(e) => setEmail(e.target.value)} required />}
          </Field>
          <ErrorAlert error={m.error} />
          <Button type="submit" busy={m.isPending} disabled={!email.includes('@')} className="w-full">Send reset link</Button>
          <p className="text-center text-sm text-ink-soft"><Link to="/login" className="font-semibold text-ink underline">Back to sign in</Link></p>
        </form>
      )}
      <DevInboxHint />
    </AuthShell>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const qc = useQueryClient()
  // Read the token once, then drop it from the address bar so it isn't left in history or shown on screen.
  const [token] = useState(() => params.get('token') ?? '')
  useEffect(() => { if (window.location.search) window.history.replaceState(null, '', '/reset-password') }, [])
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const m = useMutation({
    mutationFn: () => api.post('/auth/reset-password', { token, newPassword: password }),
    onSuccess: () => qc.setQueryData(meKey, null), // the server signed out every session, including this one
  })
  const fe = m.error instanceof ApiError ? m.error.fieldErrors : {}
  const mismatch = confirm.length > 0 && confirm !== password

  if (!token) return (
    <AuthShell title="Reset link missing">
      <Alert tone="warn" title="Open the link from your email">This page needs the full link from the reset email. You can request a new one.</Alert>
      <Link to="/forgot-password" className="mt-3 inline-block text-sm font-semibold underline">Request a new link</Link>
    </AuthShell>
  )

  return (
    <AuthShell title="Choose a new password" subtitle="This signs you out on every device.">
      {m.isSuccess ? (
        <div className="space-y-3">
          <Alert tone="info" title="Password changed">You can now sign in with your new password.</Alert>
          <Link to="/login" className="inline-block font-semibold underline">Log in</Link>
        </div>
      ) : (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (!mismatch) m.mutate() }} noValidate>
          <Field label="New password" hint="At least 10 characters." error={fe.password}>
            {(id, d) => <PasswordInput id={id} aria-describedby={d} autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required />}
          </Field>
          <Field label="Repeat new password" error={mismatch ? ['The passwords don’t match.'] : undefined}>
            {(id, d) => <PasswordInput id={id} aria-describedby={d} autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required />}
          </Field>
          {m.error && !(m.error instanceof ApiError && m.error.status === 400) && (
            <>
              <ErrorAlert error={m.error} />
              <Link to="/forgot-password" className="inline-block text-sm font-semibold underline">Request a new link</Link>
            </>
          )}
          <Button type="submit" busy={m.isPending} disabled={!password || mismatch} className="w-full">Save new password</Button>
        </form>
      )}
    </AuthShell>
  )
}
