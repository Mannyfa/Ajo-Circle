import type { ReactNode } from 'react'
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { brand } from '../app/brand'
import { useLogout, useMe } from '../hooks/useMe'
import { NotificationBell } from '../features/notifications/NotificationPages'
import { Alert, Loading } from './ui'

function Logo() {
  return (
    <Link to="/" className="flex items-center gap-2 font-extrabold tracking-tight text-ink">
      <img src="/favicon.svg" alt="" className="size-7" />
      <span>{brand.name}</span>
    </Link>
  )
}

export function PublicLayout() {
  const { data: me } = useMe()
  return (
    <div className="min-h-dvh flex flex-col">
      <header className="mx-auto flex w-full max-w-5xl items-center justify-between px-4 py-4">
        <Logo />
        <nav className="flex items-center gap-1 text-sm font-semibold">
          {me ? (
            <Link to="/app" className="rounded-xl bg-ink px-4 py-2.5 text-white">Open app</Link>
          ) : (
            <>
              <Link to="/login" className="rounded-xl px-3 py-2.5 hover:bg-ink/5">Sign in</Link>
              <Link to="/register" className="rounded-xl bg-ink px-4 py-2.5 text-white">Get started</Link>
            </>
          )}
        </nav>
      </header>
      <main className="flex-1"><Outlet /></main>
      <Footer />
    </div>
  )
}

const YEAR = new Date().getFullYear()

function Footer() {
  return (
    <footer className="border-t border-line px-4 py-6 text-xs text-ink-soft">
      <div className="mx-auto flex max-w-5xl flex-wrap gap-x-4 gap-y-2">
        <span>© {YEAR} {brand.name}</span>
        <Link to="/trust" className="hover:text-ink">Trust & Safety</Link>
        <Link to="/terms" className="hover:text-ink">Terms</Link>
        <Link to="/privacy" className="hover:text-ink">Privacy</Link>
        <span className="basis-full sm:basis-auto">{brand.name} records ajo activity. It does not hold, collect or transfer money.</span>
      </div>
    </footer>
  )
}

/** Signed-in shell. Mobile: top bar + bottom tabs. Desktop: top nav. */
export function AppLayout() {
  const { data: me, isLoading } = useMe()
  const location = useLocation()
  const navigate = useNavigate()
  const logout = useLogout()

  if (isLoading) return <Loading />
  if (!me) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />

  const tab = ({ isActive }: { isActive: boolean }) =>
    `flex-1 sm:flex-none rounded-xl px-3 py-2 text-center text-sm font-semibold ${isActive ? 'bg-ink text-white' : 'text-ink-soft hover:bg-ink/5'}`

  return (
    <div className="min-h-dvh pb-20 sm:pb-0">
      <header className="sticky top-0 z-10 border-b border-line bg-paper/95 backdrop-blur">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-3">
          <Logo />
          <nav className="hidden gap-1 sm:flex">
            <NavLink to="/app" end className={tab}>My groups</NavLink>
            <NavLink to="/app/groups/new" className={tab}>New group</NavLink>
            <NavLink to="/app/disputes" className={tab}>Disputes</NavLink>
          </nav>
          <div className="flex items-center gap-1 text-sm">
            <NotificationBell />
            <Link to="/app/settings" className="flex min-h-10 items-center rounded-xl px-2 text-ink-soft hover:bg-ink/5 hover:text-ink" aria-label={`Settings for ${me.displayName}`}>
              <span className="hidden max-w-36 truncate sm:inline">{me.displayName}</span>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="size-5 sm:hidden" aria-hidden="true">
                <circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" />
              </svg>
            </Link>
            <button
              className="rounded-xl px-3 py-2 font-semibold text-ink-soft hover:bg-ink/5"
              onClick={() => { navigate('/'); logout.mutate() }}
            >Sign out</button>
          </div>
        </div>
      </header>
      {!me.contactVerified && (
        <div className="mx-auto max-w-5xl px-4 pt-4">
          <Alert tone="warn" title="Verify your email to create or join groups.">
            <Link to="/verify" className="font-semibold underline">Enter your verification code</Link>
          </Alert>
        </div>
      )}
      <main className="mx-auto max-w-5xl px-4 py-5"><Outlet /></main>
      <nav className="fixed inset-x-0 bottom-0 flex gap-2 border-t border-line bg-card px-4 py-2 sm:hidden" aria-label="Primary">
        <NavLink to="/app" end className={tab}>My groups</NavLink>
        <NavLink to="/app/groups/new" className={tab}>New group</NavLink>
        <NavLink to="/app/disputes" className={tab}>Disputes</NavLink>
      </nav>
    </div>
  )
}

export function PageHeader({ title, subtitle, actions, back }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; back?: { to: string; label: string } }) {
  return (
    <div className="mb-5">
      {back && <Link to={back.to} className="mb-2 inline-block text-sm font-semibold text-ink-soft hover:text-ink">← {back.label}</Link>}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-2xl font-extrabold tracking-tight break-words">{title}</h1>
          {subtitle && <div className="mt-1 text-sm text-ink-soft">{subtitle}</div>}
        </div>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </div>
    </div>
  )
}
