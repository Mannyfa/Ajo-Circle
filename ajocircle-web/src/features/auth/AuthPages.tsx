import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { brand } from '../../app/brand'
import { Alert, Button, Card, ErrorAlert, Field, Loading, TextInput } from '../../components/ui'
import { meKey, useMe } from '../../hooks/useMe'
import { api, ApiError } from '../../lib/api'
import type { Me } from '../../types/api'

/** Only allow in-app relative redirects (no open redirect). */
function safeNext(raw: string | null, fallback = '/app') {
  return raw && raw.startsWith('/') && !raw.startsWith('//') ? raw : fallback
}

function AuthShell({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <div className="mx-auto max-w-md px-4 py-8">
      <h1 className="text-2xl font-extrabold">{title}</h1>
      {subtitle && <p className="mt-1 text-sm text-ink-soft">{subtitle}</p>}
      <Card className="mt-5">{children}</Card>
    </div>
  )
}

export function RegisterPage() {
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [form, setForm] = useState({ displayName: '', email: '', password: '', acceptTerms: false })
  const m = useMutation({
    mutationFn: () => api.post<Me>('/auth/register', form),
    onSuccess: (me) => {
      qc.setQueryData(meKey, me)
      navigate(`/verify?next=${encodeURIComponent(next)}`)
    },
  })
  const fe = m.error instanceof ApiError ? m.error.fieldErrors : {}

  return (
    <AuthShell title="Create your account" subtitle={`Free to use while ${brand.name} is in pilot.`}>
      <form className="space-y-4" onSubmit={(e: FormEvent) => { e.preventDefault(); m.mutate() }} noValidate>
        <Field label="Your name" hint="Shown to members of your groups." error={fe.displayName}>
          {(id, d) => <TextInput id={id} aria-describedby={d} autoComplete="name" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} required />}
        </Field>
        <Field label="Email" error={fe.email}>
          {(id, d) => <TextInput id={id} aria-describedby={d} type="email" autoComplete="email" inputMode="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required />}
        </Field>
        <Field label="Password" hint="At least 10 characters." error={fe.password}>
          {(id, d) => <TextInput id={id} aria-describedby={d} type="password" autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />}
        </Field>
        <label className="flex items-start gap-3 text-sm">
          <input type="checkbox" className="mt-1 size-4 accent-brand" checked={form.acceptTerms} onChange={(e) => setForm({ ...form, acceptTerms: e.target.checked })} />
          <span>I accept the <Link to="/terms" className="font-semibold underline">Terms of Service</Link> and <Link to="/privacy" className="font-semibold underline">Privacy Notice</Link>.</span>
        </label>
        {fe.acceptTerms && <p className="text-xs font-medium text-tone-danger">{fe.acceptTerms[0]}</p>}
        {m.error && !(m.error instanceof ApiError && m.error.status === 400) && <ErrorAlert error={m.error} />}
        <Button type="submit" busy={m.isPending} className="w-full">Create account</Button>
      </form>
      <p className="mt-4 text-center text-sm text-ink-soft">Already have an account? <Link to={`/login?next=${encodeURIComponent(next)}`} className="font-semibold text-ink underline">Sign in</Link></p>
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
      navigate(me.contactVerified ? next : `/verify?next=${encodeURIComponent(next)}`)
    },
  })
  if (me && !m.isSuccess) return <Navigate to={next} replace />

  return (
    <AuthShell title="Sign in">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); m.mutate() }}>
        <Field label="Email">
          {(id) => <TextInput id={id} type="email" autoComplete="email" inputMode="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required />}
        </Field>
        <Field label="Password">
          {(id) => <TextInput id={id} type="password" autoComplete="current-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />}
        </Field>
        <p className="-mt-2 text-right text-sm"><Link to="/forgot-password" className="font-semibold text-ink-soft underline hover:text-ink">Forgot your password?</Link></p>
        <ErrorAlert error={m.error} />
        <Button type="submit" busy={m.isPending} className="w-full">Sign in</Button>
      </form>
      <p className="mt-4 text-center text-sm text-ink-soft">New here? <Link to={`/register?next=${encodeURIComponent(next)}`} className="font-semibold text-ink underline">Create an account</Link></p>
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
    onSuccess: (me) => { qc.setQueryData(meKey, me); navigate(next) },
  })
  const resend = useMutation({ mutationFn: () => api.post('/auth/resend-verification') })

  if (isLoading) return <Loading />
  if (!me) return <Navigate to={`/login?next=${encodeURIComponent('/verify')}`} replace />
  if (me.contactVerified && !verify.isSuccess) return <Navigate to={next} replace />

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
          <Link to="/login" className="inline-block font-semibold underline">Sign in</Link>
        </div>
      ) : (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (!mismatch) m.mutate() }} noValidate>
          <Field label="New password" hint="At least 10 characters." error={fe.password}>
            {(id, d) => <TextInput id={id} aria-describedby={d} type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required />}
          </Field>
          <Field label="Repeat new password" error={mismatch ? ['The passwords don’t match.'] : undefined}>
            {(id, d) => <TextInput id={id} aria-describedby={d} type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required />}
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
