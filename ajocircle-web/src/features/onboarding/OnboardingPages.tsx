import { CheckCircle2, Circle, Layers, Link2, ShieldCheck, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { brand } from '../../app/brand'
import { usePageMeta } from '../../app/usePageMeta'
import { ctaPrimary, ctaSecondary } from '../../components/publicStyles'
import { Card, Loading } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { homeFor } from '../../lib/workspace'
import type { Me } from '../../types/api'
import { JoinWithInvite } from '../member/MemberPages'

type Step = { icon: LucideIcon; title: string; text: string; done: boolean; action?: ReactNode }

function Checklist({ steps }: { steps: Step[] }) {
  return (
    <ol className="space-y-3">
      {steps.map((s, i) => (
        <li key={s.title} className={`flex gap-4 rounded-[var(--radius-card)] border bg-card p-4 sm:p-5 ${s.done ? 'border-line' : 'border-teal/40'}`}>
          <span aria-hidden className={`grid size-10 shrink-0 place-items-center rounded-full ${s.done ? 'bg-lime text-ink' : 'bg-teal-soft text-teal'}`}>
            {s.done ? <CheckCircle2 className="size-5" /> : <s.icon className="size-5" />}
          </span>
          <div className="min-w-0 flex-1">
            <p className="font-mono text-[11px] uppercase tracking-[0.08em] text-ink-soft">Step {i + 1}{s.done ? ' · done' : ''}</p>
            <h2 className="mt-0.5 font-semibold">{s.title}</h2>
            <p className="mt-1 text-sm text-ink-soft">{s.text}</p>
            {!s.done && s.action && <div className="mt-3">{s.action}</div>}
          </div>
        </li>
      ))}
    </ol>
  )
}

/** Signed-in, email-verified, and in the right onboarding for their role. */
function useOnboardingGuard(role: Me['role']) {
  const { data: me, isLoading } = useMe()
  const location = useLocation()
  if (isLoading) return { el: <Loading /> }
  if (!me) return { el: <Navigate to={`/login?next=${encodeURIComponent(location.pathname)}`} replace /> }
  if (!me.contactVerified) return { el: <Navigate to={`/verify-account?next=${encodeURIComponent(location.pathname)}`} replace /> }
  if (me.role !== role) return { el: <Navigate to={me.role === 'Organizer' ? '/onboarding/organizer' : '/onboarding/member'} replace /> }
  return { me }
}

function Shell({ eyebrow, title, intro, children, me }: { eyebrow: string; title: string; intro: string; children: ReactNode; me: Me }) {
  return (
    <div className="bg-paper">
      <div className="mx-auto max-w-2xl px-4 py-10 sm:px-6 sm:py-14">
        <p className="eyebrow">{eyebrow}</p>
        <h1 className="display mt-4 text-4xl sm:text-5xl">{title}</h1>
        <p className="mt-2 text-ink-soft">{intro}</p>
        <div className="mt-8">{children}</div>
        <div className="mt-8 flex flex-col gap-3 sm:flex-row">
          <Link to={homeFor(me)} className={ctaSecondary}>Go to my dashboard</Link>
        </div>
      </div>
    </div>
  )
}

export function OrganizerOnboardingPage() {
  usePageMeta('Set up your organizer account')
  const g = useOnboardingGuard('Organizer')
  if (!g.me) return g.el
  const me = g.me
  const verified = me.identityLevel === 'Organizer'
  return (
    <Shell me={me} eyebrow="Organizer setup" title={`Welcome, ${me.displayName.split(/\s+/)[0]}`}
      intro={`Three steps and you can run your first group on ${brand.name}. Your workspace keeps every group you run, its members, records and reports in one place.`}>
      <Checklist steps={[
        { icon: CheckCircle2, title: 'Verify your email', text: me.email, done: true },
        {
          icon: ShieldCheck, title: 'Verify your identity as an organizer', done: verified,
          text: 'Your phone number, an ID document, your BVN and a passport photo. Members trust organizers with their money, so our team checks every organizer. It usually takes a working day.',
          action: <Link to="/app/verification" className={ctaPrimary}>Start verification</Link>,
        },
        {
          icon: Layers, title: 'Create your first group', done: false,
          text: verified ? 'Choose rotating ajo or fixed-term savings, set the rules and schedule, then share the invite link.' : 'Available once your identity check is approved.',
          action: verified ? <Link to="/app/groups/new" className={ctaPrimary}>Create a group</Link> : undefined,
        },
      ]} />
    </Shell>
  )
}

export function MemberOnboardingPage() {
  usePageMeta('Set up your member account')
  const g = useOnboardingGuard('Member')
  if (!g.me) return g.el
  const me = g.me
  const verified = me.identityLevel !== 'None'
  return (
    <Shell me={me} eyebrow="Member setup" title={`Welcome, ${me.displayName.split(/\s+/)[0]}`}
      intro="You’ll see your groups’ rules, your payment dates and every record about you. You only see groups you’ve joined.">
      <Checklist steps={[
        { icon: CheckCircle2, title: 'Verify your email', text: me.email, done: true },
        {
          icon: ShieldCheck, title: 'Verify your identity', done: verified,
          text: 'Your phone number and one ID document. Our team checks it, usually within a working day.',
          action: <Link to="/member/verification" className={ctaPrimary}>Start verification</Link>,
        },
        {
          icon: Link2, title: 'Join your group', done: false,
          text: 'Paste the invite link your organizer sent. You’ll read the group’s rules before you decide to join.',
          action: <Card className="bg-paper"><JoinWithInvite compact /></Card>,
        },
      ]} />
      {!verified && <p className="mt-4 flex items-center gap-2 text-sm text-ink-soft"><Circle aria-hidden className="size-3" />You can open an invite now to read the rules; joining needs the identity check.</p>}
    </Shell>
  )
}
