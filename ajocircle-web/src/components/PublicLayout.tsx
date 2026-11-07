import { Menu, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { brand } from '../app/brand'
import { useMe } from '../hooks/useMe'
import { ctaPrimary, publicLinks } from './publicStyles'

export function Logo({ to = '/', inverse }: { to?: string; inverse?: boolean }) {
  return (
    <Link to={to} className={`flex items-center gap-2.5 text-[15px] font-bold tracking-tight ${inverse ? 'text-white' : 'text-ink'}`}>
      <img src="/favicon.svg" alt="" className="size-8" />
      <span>{brand.name}</span>
    </Link>
  )
}

/** Public site shell: sticky header (border appears on scroll), accessible mobile menu, four-column footer. */
export function PublicLayout() {
  const { data: me } = useMe()
  const { pathname } = useLocation()
  // The menu belongs to the page it was opened on, so navigating closes it without an effect.
  const [openOn, setOpenOn] = useState<string | null>(null)
  const open = openOn === pathname
  const setOpen = (v: boolean | ((o: boolean) => boolean)) => setOpenOn((typeof v === 'function' ? v(open) : v) ? pathname : null)
  const [scrolled, setScrolled] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 4)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  // New pages start at the top.
  useEffect(() => { window.scrollTo(0, 0) }, [pathname])

  // Menu focus: first link on open, Escape closes and returns focus to the toggle.
  useEffect(() => {
    if (!open) return
    menuRef.current?.querySelector<HTMLElement>('a')?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpenOn(null); toggleRef.current?.focus() } }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open])

  const link = ({ isActive }: { isActive: boolean }) =>
    `inline-flex min-h-11 items-center rounded-lg px-3 text-sm font-medium transition-colors ${isActive ? 'text-teal' : 'text-ink-soft hover:text-ink'}`

  return (
    <div className="flex min-h-dvh flex-col bg-card">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:shadow-[var(--shadow-raised)]">Skip to content</a>
      <header className={`sticky top-0 z-30 bg-card/95 backdrop-blur transition-shadow ${scrolled || open ? 'border-b border-line shadow-[var(--shadow-card)]' : 'border-b border-transparent'}`}>
        <div className="mx-auto flex h-16 w-full max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <Logo />
          <nav className="hidden items-center gap-1 lg:flex" aria-label="Main">
            {publicLinks.map((l) => <NavLink key={l.to} to={l.to} className={link}>{l.label}</NavLink>)}
          </nav>
          <div className="hidden items-center gap-2 lg:flex">
            {me ? (
              <Link to="/app/dashboard" className={ctaPrimary}>Open workspace</Link>
            ) : (<>
              <NavLink to="/login" className={link}>Log in</NavLink>
              <Link to="/register" className={ctaPrimary}>Get Started</Link>
            </>)}
          </div>
          <button ref={toggleRef} type="button" className="grid size-11 place-items-center rounded-lg text-ink hover:bg-paper lg:hidden"
            aria-expanded={open} aria-controls="public-menu" aria-label={open ? 'Close menu' : 'Open menu'} onClick={() => setOpen((o) => !o)}>
            {open ? <X aria-hidden className="size-6" /> : <Menu aria-hidden className="size-6" />}
          </button>
        </div>
        {open && (
          <div id="public-menu" ref={menuRef} className="max-h-[calc(100dvh-4rem)] overflow-y-auto border-t border-line bg-card px-4 pb-[max(1.25rem,env(safe-area-inset-bottom))] pt-2 lg:hidden">
            <nav aria-label="Main">
              <ul className="divide-y divide-line">
                {publicLinks.map((l) => (
                  <li key={l.to}><NavLink to={l.to} className={({ isActive }) => `flex min-h-12 items-center text-base font-medium ${isActive ? 'text-teal' : 'text-ink'}`}>{l.label}</NavLink></li>
                ))}
                <li><NavLink to="/about" className={({ isActive }) => `flex min-h-12 items-center text-base font-medium ${isActive ? 'text-teal' : 'text-ink'}`}>About</NavLink></li>
                {!me && <li><NavLink to="/login" className="flex min-h-12 items-center text-base font-medium text-ink">Log in</NavLink></li>}
              </ul>
            </nav>
            <Link to={me ? '/app/dashboard' : '/register'} className={`${ctaPrimary} mt-3 w-full`}>{me ? 'Open workspace' : 'Get Started'}</Link>
          </div>
        )}
      </header>
      <main id="main" className="flex-1"><Outlet /></main>
      <Footer />
    </div>
  )
}

const YEAR = new Date().getFullYear()

function Footer() {
  const cols: { title: string; links: { to: string; label: string; external?: boolean }[] }[] = [
    { title: 'Product', links: [...publicLinks, { to: '/register', label: 'Get Started' }] },
    { title: 'Company', links: [{ to: '/about', label: 'About' }, { to: '/contact', label: 'Contact' }] },
    { title: 'Legal', links: [{ to: '/privacy', label: 'Privacy Notice' }, { to: '/terms', label: 'Terms of Service' }, { to: '/financial-services-disclaimer', label: 'Financial services disclaimer' }] },
    ...(brand.social.length ? [{ title: 'Connect', links: brand.social.map((s) => ({ to: s.href, label: s.label, external: true })) }] : []),
  ]
  return (
    <footer className="bg-brand text-white/80">
      <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <div className="grid gap-8 sm:grid-cols-2 lg:grid-cols-[1.4fr_repeat(3,1fr)]">
          <div className="max-w-xs">
            <Logo inverse />
            <p className="mt-3 text-sm">Records and schedules for community savings groups.</p>
          </div>
          {cols.map((c) => (
            <div key={c.title}>
              <h2 className="text-xs font-semibold uppercase tracking-wider text-white">{c.title}</h2>
              <ul className="mt-3 space-y-1">
                {c.links.map((l) => (
                  <li key={l.to}>{l.external
                    ? <a href={l.to} rel="noopener noreferrer" target="_blank" className="inline-flex min-h-9 items-center text-sm hover:text-white">{l.label}</a>
                    : <Link to={l.to} className="inline-flex min-h-9 items-center text-sm hover:text-white">{l.label}</Link>}</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
        <div className="mt-10 flex flex-col gap-2 border-t border-white/15 pt-6 text-xs sm:flex-row sm:justify-between">
          <span>© {YEAR} {brand.name}</span>
          <span>{brand.name} records ajo activity. It does not hold, collect, transfer or guarantee money.</span>
        </div>
      </div>
    </footer>
  )
}
