import { Menu, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { brand } from '../app/brand'
import { useMe } from '../hooks/useMe'
import { ctaPrimary, publicLinks } from './publicStyles'

export function Logo({ to = '/', inverse }: { to?: string; inverse?: boolean }) {
  return (
    <Link to={to} className={`inline-flex items-center gap-2 text-[15px] font-semibold tracking-[-0.02em] ${inverse ? 'text-white' : 'text-ink'}`}>
      <img src="/favicon.svg" alt="" className="size-7" />
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
    `inline-flex min-h-11 items-center text-[13px] transition-colors ${isActive ? 'text-ink' : 'text-ink-soft hover:text-ink'}`
  const smallCta = 'inline-flex h-9 items-center justify-center gap-1.5 rounded-lg bg-lime px-3.5 text-[13px] font-medium text-ink transition-[background-color,transform] duration-150 hover:bg-lime-hover active:scale-[0.98]'

  return (
    <div className="flex min-h-dvh flex-col bg-paper">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:shadow-[var(--shadow-raised)]">Skip to content</a>
      <header className={`sticky top-0 z-30 border-b bg-paper/80 backdrop-blur-md transition-colors duration-200 ${scrolled || open ? 'border-line' : 'border-transparent'}`}>
        <div className="frame flex h-16 items-center justify-between gap-4">
          <Logo />
          <nav className="hidden items-center gap-8 lg:flex" aria-label="Main">
            {publicLinks.map((l) => <NavLink key={l.to} to={l.to} className={link}>{l.label}</NavLink>)}
          </nav>
          <div className="hidden items-center gap-5 lg:flex">
            {me ? (
              <Link to="/app/dashboard" className={smallCta}>Open workspace</Link>
            ) : (<>
              <NavLink to="/login" className={link}>Log in</NavLink>
              <Link to="/register" className={smallCta}>Get started</Link>
            </>)}
          </div>
          <button ref={toggleRef} type="button" className="grid size-11 place-items-center rounded-lg text-ink hover:bg-subtle lg:hidden"
            aria-expanded={open} aria-controls="public-menu" aria-label={open ? 'Close menu' : 'Open menu'} onClick={() => setOpen((o) => !o)}>
            {open ? <X aria-hidden className="size-6" /> : <Menu aria-hidden className="size-6" />}
          </button>
        </div>
        {open && (
          <div id="public-menu" ref={menuRef} className="max-h-[calc(100dvh-4rem)] overflow-y-auto border-t border-line bg-paper px-4 pb-[max(1.25rem,env(safe-area-inset-bottom))] pt-2 sm:px-6 lg:hidden">
            <nav aria-label="Main">
              <ul className="divide-y divide-line">
                {[...publicLinks, { to: '/about', label: 'About' }].map((l) => (
                  <li key={l.to}><NavLink to={l.to} className={({ isActive }) => `flex min-h-12 items-center text-base ${isActive ? 'font-medium text-ink' : 'text-ink-soft'}`}>{l.label}</NavLink></li>
                ))}
                {!me && <li><NavLink to="/login" className="flex min-h-12 items-center text-base text-ink-soft">Log in</NavLink></li>}
              </ul>
            </nav>
            <Link to={me ? '/app/dashboard' : '/register'} className={`${ctaPrimary} mt-3 w-full`}>{me ? 'Open workspace' : 'Get started'}</Link>
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
    { title: 'Product', links: [...publicLinks, { to: '/register', label: 'Get started' }] },
    { title: 'Company', links: [{ to: '/about', label: 'About' }, { to: '/contact', label: 'Contact' }] },
    { title: 'Legal', links: [{ to: '/privacy', label: 'Privacy notice' }, { to: '/terms', label: 'Terms of service' }, { to: '/financial-services-disclaimer', label: 'Financial disclaimer' }] },
    ...(brand.social.length ? [{ title: 'Connect', links: brand.social.map((s) => ({ to: s.href, label: s.label, external: true })) }] : []),
  ]
  const a = 'inline-flex min-h-9 items-center text-[13px] text-ink-soft transition-colors hover:text-ink'
  return (
    <footer className="mt-auto border-t border-line">
      <div className="frame grid gap-10 py-12 sm:grid-cols-2 lg:grid-cols-[1.5fr_repeat(3,1fr)]">
        <div className="max-w-xs space-y-3">
          <Logo />
          <p className="text-[13px] leading-relaxed text-ink-soft">Records and schedules for ajo, esusu and adashe groups. We keep the records; your money stays with your group.</p>
        </div>
        {cols.map((c) => (
          <div key={c.title}>
            <h2 className="eyebrow">{c.title}</h2>
            <ul className="mt-3">
              {c.links.map((l) => (
                <li key={l.to}>{l.external
                  ? <a href={l.to} rel="noopener noreferrer" target="_blank" className={a}>{l.label}</a>
                  : <Link to={l.to} className={a}>{l.label}</Link>}</li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      <div className="border-t border-line">
        <div className="frame flex flex-col gap-2 py-6 text-xs text-ink-faint sm:flex-row sm:justify-between">
          <span>© {YEAR} {brand.name}</span>
          <span>{brand.name} records ajo activity. It does not hold, collect, transfer or guarantee money.</span>
        </div>
      </div>
    </footer>
  )
}
