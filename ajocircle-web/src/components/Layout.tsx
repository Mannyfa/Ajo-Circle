import {
  ArrowLeft, BadgeCheck, Bell, ChevronRight, CircleHelp, Layers, LayoutDashboard, LogOut, Menu, Scale, Search, Settings, ShieldCheck, X,
  type LucideIcon,
} from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { NotificationBell } from '../features/notifications/NotificationPages'
import { usePageMeta } from '../app/usePageMeta'
import { useLogout, useMe } from '../hooks/useMe'
import { Logo } from './PublicLayout'
import { Alert, Loading } from './ui'
import type { Me } from '../types/api'

type NavItem = { to: string; label: string; icon: LucideIcon; end?: boolean }

function navFor(me: Me) {
  const workspace: NavItem[] = [
    { to: '/app/dashboard', label: 'Dashboard', icon: LayoutDashboard },
    { to: '/app/groups', label: 'My Groups', icon: Layers },
    { to: '/app/disputes', label: 'Disputes', icon: Scale },
    { to: '/app/notifications', label: 'Notifications', icon: Bell },
  ]
  const account: NavItem[] = [
    ...(me.identityLevel !== 'Organizer' && !me.isOperations ? [{ to: '/app/verification', label: 'Verify identity', icon: BadgeCheck }] : []),
    ...(me.isOperations ? [{ to: '/operations', label: 'Operations', icon: ShieldCheck }] : []),
    { to: '/app/settings', label: 'Settings', icon: Settings },
    { to: '/app/help', label: 'Help', icon: CircleHelp },
  ]
  return { workspace, account }
}

/** Page names for the header title on screens where the sidebar is hidden. */
const sectionTitles: [RegExp, string][] = [
  [/^\/app\/dashboard/, 'Dashboard'], [/^\/app\/groups\/new/, 'New group'], [/^\/app\/groups\/[^/]+/, 'Group'], [/^\/app\/groups/, 'My Groups'],
  [/^\/app\/disputes/, 'Disputes'], [/^\/app\/notifications/, 'Notifications'], [/^\/app\/settings/, 'Settings'], [/^\/app\/help/, 'Help'],
  [/^\/app\/verification/, 'Verify identity'], [/^\/operations/, 'Operations'],
]

/**
 * Signed-in shell, per the spec's breakpoints:
 *  - desktop (≥1280): full labelled sidebar;
 *  - tablet / small laptop (768–1279): compact icon rail with short labels;
 *  - phone (<768): top bar plus bottom tabs (four destinations + More), with More opening a full navigation drawer.
 */
export function AppLayout() {
  const { data: me, isLoading } = useMe()
  const location = useLocation()
  const navigate = useNavigate()
  const logout = useLogout()
  // The drawer belongs to the page it was opened on, so navigating closes it.
  const [drawerOn, setDrawerOn] = useState<string | null>(null)
  const drawer = drawerOn === location.pathname
  const title = sectionTitles.find(([re]) => re.test(location.pathname))?.[1] ?? ''
  usePageMeta(title)
  const setDrawer = (v: boolean) => setDrawerOn(v ? location.pathname : null)

  if (isLoading) return <Loading />
  if (!me) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />

  const { workspace, account } = navFor(me)
  const signOut = () => { navigate('/'); logout.mutate() }

  return (
    <div className="min-h-dvh md:pl-24 xl:pl-64">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:shadow-[var(--shadow-raised)]">Skip to content</a>

      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-64 flex-col bg-brand text-white xl:flex" aria-label="Main navigation">
        <div className="flex h-16 items-center px-5"><Logo to="/app/dashboard" inverse /></div>
        <nav className="flex-1 space-y-6 overflow-y-auto px-3 py-4">
          <SideSection items={workspace} />
          <div>
            <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wider text-white/50">Account</p>
            <SideSection items={account} />
          </div>
        </nav>
        <div className="border-t border-white/10 p-3">
          <ProfileRow me={me} onSignOut={signOut} inverse />
        </div>
      </aside>

      {/* Tablet rail */}
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-24 flex-col items-stretch bg-brand text-white md:flex xl:hidden" aria-label="Main navigation">
        <Link to="/app/dashboard" className="grid h-16 place-items-center" aria-label="Dashboard"><img src="/favicon.svg" alt="" className="size-9" /></Link>
        <nav className="flex-1 overflow-y-auto px-2 py-2">
          <ul className="space-y-1">
            {[...workspace, ...account].map((item) => (
              <li key={item.to}>
                <NavLink to={item.to} end={item.end}
                  className={({ isActive }) => `flex min-h-14 flex-col items-center justify-center gap-1 rounded-lg px-1 text-center text-[11px] font-medium leading-tight transition-colors ${isActive ? 'bg-white/12 text-white' : 'text-white/65 hover:bg-white/8 hover:text-white'}`}>
                  {({ isActive }) => (<><item.icon aria-hidden className={`size-5 ${isActive ? 'text-mint' : ''}`} />{item.label}</>)}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <button onClick={signOut} className="mx-2 mb-3 flex min-h-14 flex-col items-center justify-center gap-1 rounded-lg text-[11px] font-medium text-white/65 hover:bg-white/8 hover:text-white">
          <LogOut aria-hidden className="size-5" />Sign out
        </button>
      </aside>

      {/* Top bar */}
      <header className="sticky top-0 z-10 border-b border-line bg-card/95 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:px-6">
          <div className="md:hidden"><Link to="/app/dashboard" aria-label="Dashboard"><img src="/favicon.svg" alt="" className="size-8" /></Link></div>
          <p className="min-w-0 flex-1 truncate text-base font-semibold md:text-lg">{title}</p>
          <GlobalSearch />
          <NotificationBell />
          <Link to="/app/settings" aria-label="Your profile and settings"
            className="hidden size-10 place-items-center rounded-full bg-teal-soft text-xs font-bold text-teal hover:ring-2 hover:ring-teal/30 sm:grid">{initials(me)}</Link>
        </div>
      </header>

      {!me.contactVerified ? (
        <Banner><Alert tone="warn" title="Verify your email to create or join groups."><Link to="/verify-account" className="font-semibold underline">Enter your verification code</Link></Alert></Banner>
      ) : me.identityLevel === 'None' && !me.isOperations && location.pathname !== '/app/verification' && (
        <Banner>
          <Alert tone="warn" title="Verify your identity to create or join groups.">
            It takes a few minutes: your phone number and an ID document (organizers also give their BVN and a passport photo).{' '}
            <Link to="/app/verification" className="font-semibold underline">Start verification</Link>
          </Alert>
        </Banner>
      )}

      <main id="main" className="mx-auto max-w-7xl px-4 pb-28 pt-6 sm:px-6 md:pb-12 md:pt-8"><Outlet /></main>

      {/* Phone bottom tabs */}
      <nav className="fixed inset-x-0 bottom-0 z-20 grid grid-cols-5 border-t border-line bg-card pb-[env(safe-area-inset-bottom)] md:hidden" aria-label="Primary">
        {workspace.map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end}
            className={({ isActive }) => `flex min-h-14 flex-col items-center justify-center gap-0.5 text-[11px] font-semibold ${isActive ? 'text-teal' : 'text-ink-soft'}`}>
            {({ isActive }) => (<><item.icon aria-hidden className="size-5" strokeWidth={isActive ? 2.4 : 2} />{({ 'My Groups': 'Groups', Notifications: 'Alerts' } as Record<string, string>)[item.label] ?? item.label}</>)}
          </NavLink>
        ))}
        <button type="button" onClick={() => setDrawer(true)} aria-haspopup="dialog" aria-expanded={drawer}
          className="flex min-h-14 flex-col items-center justify-center gap-0.5 text-[11px] font-semibold text-ink-soft">
          <Menu aria-hidden className="size-5" />More
        </button>
      </nav>

      <NavDrawer open={drawer} onClose={() => setDrawer(false)} me={me} sections={[['Workspace', workspace], ['Account', account]]} onSignOut={signOut} />
    </div>
  )
}

function initials(me: Me) {
  return me.displayName.split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase()
}

/** Searches the user's groups by name (the groups page reads ?q=). Hidden on phones, where the groups page has its own search. */
function GlobalSearch() {
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const submit = (e: FormEvent) => { e.preventDefault(); navigate(`/app/groups${q.trim() ? `?q=${encodeURIComponent(q.trim())}` : ''}`) }
  return (
    <form role="search" onSubmit={submit} className="relative hidden w-72 lg:block">
      <Search aria-hidden className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
      <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search your groups" aria-label="Search your groups"
        className="h-10 w-full rounded-lg border border-line bg-paper pl-9 pr-3 text-sm placeholder:text-ink-soft focus:border-teal focus:bg-card focus:outline-none focus:ring-2 focus:ring-teal/15" />
    </form>
  )
}

function SideSection({ items }: { items: NavItem[] }) {
  return (
    <ul className="space-y-0.5">
      {items.map((item) => (
        <li key={item.to}>
          <NavLink to={item.to} end={item.end}
            className={({ isActive }) => `relative flex min-h-11 items-center gap-3 rounded-lg px-3 text-sm font-medium transition-colors ${isActive ? 'bg-white/10 text-white before:absolute before:inset-y-2 before:left-0 before:w-1 before:rounded-r before:bg-mint' : 'text-white/70 hover:bg-white/5 hover:text-white'}`}>
            {({ isActive }) => (<><item.icon aria-hidden className={`size-[18px] shrink-0 ${isActive ? 'text-mint' : ''}`} />{item.label}</>)}
          </NavLink>
        </li>
      ))}
    </ul>
  )
}

function ProfileRow({ me, onSignOut, inverse }: { me: Me; onSignOut: () => void; inverse?: boolean }) {
  return (
    <div className="flex items-center gap-3 rounded-lg px-2 py-2">
      <span aria-hidden className={`grid size-9 shrink-0 place-items-center rounded-full text-xs font-bold ${inverse ? 'bg-white/10 text-mint' : 'bg-teal-soft text-teal'}`}>{initials(me)}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-semibold">{me.displayName}</span>
        <span className={`block truncate text-xs ${inverse ? 'text-white/55' : 'text-ink-soft'}`}>{me.email}</span>
      </span>
      <button onClick={onSignOut} aria-label="Sign out" title="Sign out"
        className={`grid size-10 place-items-center rounded-lg ${inverse ? 'text-white/60 hover:bg-white/10 hover:text-white' : 'text-ink-soft hover:bg-ink/5 hover:text-ink'}`}>
        <LogOut aria-hidden className="size-[18px]" />
      </button>
    </div>
  )
}

/** Full navigation as a modal sheet on phones. Native <dialog> gives focus containment and Escape-to-close. */
function NavDrawer({ open, onClose, me, sections, onSignOut }: { open: boolean; onClose: () => void; me: Me; sections: [string, NavItem[]][]; onSignOut: () => void }) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) d.showModal()
    if (!open && d.open) d.close()
  }, [open])
  return (
    <dialog ref={ref} onClose={onClose} onClick={(e) => { if (e.target === ref.current) onClose() }} aria-label="Navigation"
      className="m-0 ml-auto h-dvh max-h-dvh w-[min(22rem,88vw)] max-w-none bg-card p-0 text-ink shadow-[var(--shadow-overlay)] backdrop:bg-brand/50 md:hidden">
      <div className="flex h-full flex-col pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)]">
        <div className="flex h-16 items-center justify-between border-b border-line px-4">
          <Logo to="/app/dashboard" />
          <button type="button" onClick={onClose} aria-label="Close navigation" className="grid size-11 place-items-center rounded-lg text-ink-soft hover:bg-paper"><X aria-hidden className="size-6" /></button>
        </div>
        <nav className="flex-1 space-y-6 overflow-y-auto px-3 py-4" aria-label="All sections">
          {sections.map(([label, items]) => (
            <div key={label}>
              <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wider text-ink-soft">{label}</p>
              <ul className="space-y-0.5">
                {items.map((item) => (
                  <li key={item.to}>
                    <NavLink to={item.to} end={item.end}
                      className={({ isActive }) => `flex min-h-12 items-center gap-3 rounded-lg px-3 text-base font-medium ${isActive ? 'bg-teal-soft text-teal' : 'text-ink hover:bg-paper'}`}>
                      <item.icon aria-hidden className="size-5 shrink-0" />{item.label}<ChevronRight aria-hidden className="ml-auto size-4 text-ink-soft" />
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
        <div className="border-t border-line p-3"><ProfileRow me={me} onSignOut={onSignOut} /></div>
      </div>
    </dialog>
  )
}

function Banner({ children }: { children: ReactNode }) {
  return <div className="mx-auto max-w-7xl px-4 pt-4 sm:px-6">{children}</div>
}

/** Phone: title and description stack, then actions full width. Larger screens: actions sit to the right. */
export function PageHeader({ title, subtitle, actions, back }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; back?: { to: string; label: string } }) {
  return (
    <div className="mb-6">
      {back && (
        <Link to={back.to} className="mb-3 inline-flex min-h-9 items-center gap-1.5 text-sm font-medium text-ink-soft hover:text-ink">
          <ArrowLeft aria-hidden className="size-4" />{back.label}
        </Link>
      )}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold tracking-tight break-words text-ink sm:text-[1.75rem]">{title}</h1>
          {subtitle && <div className="mt-1 text-sm text-ink-soft">{subtitle}</div>}
        </div>
        {actions && <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap [&>*]:w-full sm:[&>*]:w-auto">{actions}</div>}
      </div>
    </div>
  )
}
