import { ArrowLeft, BadgeCheck, LayoutDashboard, LogOut, PlusCircle, Scale, Settings, ShieldCheck, UserRound, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { brand } from '../app/brand'
import { NotificationBell } from '../features/notifications/NotificationPages'
import { useLogout, useMe } from '../hooks/useMe'
import { Alert, Loading } from './ui'

function Logo({ to = '/' }: { to?: string }) {
  return (
    <Link to={to} className="flex items-center gap-2.5 text-[15px] font-bold tracking-tight text-ink">
      <img src="/favicon.svg" alt="" className="size-8" />
      <span>{brand.name}</span>
    </Link>
  )
}

export function PublicLayout() {
  const { data: me } = useMe()
  return (
    <div className="flex min-h-dvh flex-col bg-card">
      <header className="border-b border-line bg-card">
        <div className="mx-auto flex w-full max-w-6xl items-center justify-between px-4 py-3 sm:px-6">
          <Logo />
          <nav className="flex items-center gap-1 text-sm font-semibold" aria-label="Account">
            {me ? (
              <Link to="/app" className="inline-flex min-h-11 items-center rounded-lg bg-brand px-4 text-white hover:bg-brand-dark">Open app</Link>
            ) : (
              <>
                <Link to="/login" className="inline-flex min-h-11 items-center rounded-lg px-3 text-ink-soft hover:bg-ink/5 hover:text-ink">Sign in</Link>
                <Link to="/register" className="inline-flex min-h-11 items-center rounded-lg bg-brand px-4 text-white hover:bg-brand-dark">Get started</Link>
              </>
            )}
          </nav>
        </div>
      </header>
      <main className="flex-1 bg-paper"><Outlet /></main>
      <Footer />
    </div>
  )
}

const YEAR = new Date().getFullYear()

function Footer() {
  return (
    <footer className="border-t border-line bg-card px-4 py-6 text-xs text-ink-soft sm:px-6">
      <div className="mx-auto flex max-w-6xl flex-wrap gap-x-5 gap-y-2">
        <span>© {YEAR} {brand.name}</span>
        <Link to="/trust" className="hover:text-ink">Trust & Safety</Link>
        <Link to="/terms" className="hover:text-ink">Terms</Link>
        <Link to="/privacy" className="hover:text-ink">Privacy</Link>
        <span className="basis-full sm:ml-auto sm:basis-auto">{brand.name} records ajo activity. It does not hold, collect or transfer money.</span>
      </div>
    </footer>
  )
}

type NavItem = { to: string; label: string; icon: LucideIcon; end?: boolean }

/**
 * Signed-in shell. Desktop (lg+): a fixed sidebar with labelled icons. Smaller screens: a top bar and a bottom tab bar
 * with at most five labelled destinations, padded for the phone's safe area.
 */
export function AppLayout() {
  const { data: me, isLoading } = useMe()
  const location = useLocation()
  const navigate = useNavigate()
  const logout = useLogout()

  if (isLoading) return <Loading />
  if (!me) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />

  const primary: NavItem[] = [
    { to: '/app', label: 'My groups', icon: LayoutDashboard, end: true },
    { to: '/app/groups/new', label: 'New group', icon: PlusCircle },
    { to: '/app/disputes', label: 'Disputes', icon: Scale },
  ]
  const account: NavItem[] = [
    ...(me.identityLevel !== 'Organizer' && !me.isOperations ? [{ to: '/app/verification', label: 'Verify identity', icon: BadgeCheck }] : []),
    ...(me.isOperations ? [{ to: '/operations', label: 'Operations', icon: ShieldCheck }] : []),
    { to: '/app/settings', label: 'Settings', icon: Settings },
  ]
  const signOut = () => { navigate('/'); logout.mutate() }
  const initials = me.displayName.split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase()

  return (
    <div className="min-h-dvh lg:pl-64">
      {/* Sidebar */}
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-64 flex-col border-r border-line bg-card lg:flex" aria-label="Main navigation">
        <div className="flex h-16 items-center border-b border-line px-5"><Logo to="/app" /></div>
        <nav className="flex-1 space-y-6 overflow-y-auto px-3 py-5">
          <SideSection items={primary} />
          <div>
            <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wider text-ink-soft">Account</p>
            <SideSection items={account} />
          </div>
        </nav>
        <div className="border-t border-line p-3">
          <div className="flex items-center gap-3 rounded-lg px-2 py-2">
            <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-full bg-brand-soft text-xs font-bold text-brand">{initials}</span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-semibold">{me.displayName}</span>
              <span className="block truncate text-xs text-ink-soft">{me.email}</span>
            </span>
            <button onClick={signOut} className="grid size-10 place-items-center rounded-lg text-ink-soft hover:bg-ink/5 hover:text-ink" aria-label="Sign out" title="Sign out">
              <LogOut aria-hidden className="size-[18px]" />
            </button>
          </div>
        </div>
      </aside>

      {/* Top bar */}
      <header className="sticky top-0 z-10 border-b border-line bg-card/95 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4 sm:px-6">
          <div className="lg:hidden"><Logo to="/app" /></div>
          <div className="hidden lg:block" />
          <div className="flex items-center gap-1">
            <NotificationBell />
            <Link to="/app/settings" className="grid size-10 place-items-center rounded-lg text-ink-soft hover:bg-ink/5 hover:text-ink lg:hidden" aria-label="Settings">
              <UserRound aria-hidden className="size-5" />
            </Link>
            <button onClick={signOut} className="hidden min-h-10 items-center gap-2 rounded-lg px-3 text-sm font-semibold text-ink-soft hover:bg-ink/5 hover:text-ink sm:inline-flex lg:hidden">
              <LogOut aria-hidden className="size-4" />Sign out
            </button>
          </div>
        </div>
      </header>

      {!me.contactVerified ? (
        <Banner><Alert tone="warn" title="Verify your email to create or join groups."><Link to="/verify" className="font-semibold underline">Enter your verification code</Link></Alert></Banner>
      ) : me.identityLevel === 'None' && !me.isOperations && location.pathname !== '/app/verification' && (
        <Banner>
          <Alert tone="warn" title="Verify your identity to create or join groups.">
            It takes a few minutes: your phone number and an ID document (organizers also give their BVN and a passport photo).{' '}
            <Link to="/app/verification" className="font-semibold underline">Start verification</Link>
          </Alert>
        </Banner>
      )}

      <main className="mx-auto max-w-6xl px-4 pb-28 pt-6 sm:px-6 lg:pb-10"><Outlet /></main>

      {/* Bottom tab bar (≤ 5 items) */}
      <nav className="fixed inset-x-0 bottom-0 z-20 grid grid-cols-4 border-t border-line bg-card pb-[env(safe-area-inset-bottom)] lg:hidden" aria-label="Primary">
        {[...primary, me.isOperations ? account.find((a) => a.to === '/operations')! : { to: '/app/settings', label: 'Account', icon: UserRound }].map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end}
            className={({ isActive }) => `flex min-h-14 flex-col items-center justify-center gap-0.5 text-[11px] font-semibold ${isActive ? 'text-brand' : 'text-ink-soft'}`}>
            {({ isActive }) => (<>
              <item.icon aria-hidden className="size-5" strokeWidth={isActive ? 2.4 : 2} />
              {item.label}
            </>)}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}

function SideSection({ items }: { items: NavItem[] }) {
  return (
    <ul className="space-y-0.5">
      {items.map((item) => (
        <li key={item.to}>
          <NavLink to={item.to} end={item.end}
            className={({ isActive }) => `group flex min-h-10 items-center gap-3 rounded-lg px-3 text-sm font-medium transition-colors ${isActive ? 'bg-brand-soft text-brand' : 'text-ink-soft hover:bg-paper hover:text-ink'}`}>
            <item.icon aria-hidden className="size-[18px] shrink-0" />
            {item.label}
          </NavLink>
        </li>
      ))}
    </ul>
  )
}

function Banner({ children }: { children: ReactNode }) {
  return <div className="mx-auto max-w-6xl px-4 pt-4 sm:px-6">{children}</div>
}

export function PageHeader({ title, subtitle, actions, back }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; back?: { to: string; label: string } }) {
  return (
    <div className="mb-6">
      {back && (
        <Link to={back.to} className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-ink-soft hover:text-ink">
          <ArrowLeft aria-hidden className="size-4" />{back.label}
        </Link>
      )}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold tracking-tight break-words text-ink">{title}</h1>
          {subtitle && <div className="mt-1 text-sm text-ink-soft">{subtitle}</div>}
        </div>
        {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
      </div>
    </div>
  )
}
