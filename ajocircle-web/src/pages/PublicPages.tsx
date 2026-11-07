import {
  AlertTriangle, ArrowRight, BookOpenCheck, CalendarClock, CheckCircle2, ChevronDown, CircleDot, ClipboardList, FileBarChart,
  FileText, History, Layers, Link2, Mail, MessageSquare, PiggyBank, Repeat, Scale, ShieldCheck, UserPlus, Users, type LucideIcon,
} from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { brand } from '../app/brand'
import { usePageMeta } from '../app/usePageMeta'
import { ctaPrimary, ctaSecondary } from '../components/publicStyles'
import { Alert } from '../components/ui'

/* ----------------------------------------------------------------------------------------------------------------
 * Shared content. Each block is used on the homepage and on its own page, so the two never drift apart.
 * -------------------------------------------------------------------------------------------------------------- */

const steps: { icon: LucideIcon; title: string; text: string }[] = [
  { icon: Layers, title: 'Create a group', text: 'Choose rotating ajo or fixed-term savings, name the group and set how many members it takes.' },
  { icon: ClipboardList, title: 'Set rules and schedule', text: 'Contribution amount, how often, start date and payout order. The full schedule is worked out for you.' },
  { icon: UserPlus, title: 'Invite members', text: 'Share a secure link. Members read the exact rules and accept them before they join.' },
  { icon: BookOpenCheck, title: 'Track records', text: 'Record contributions and payouts as they happen. Members confirm, or raise a correction if something is wrong.' },
]

const features: { icon: LucideIcon; title: string; text: string }[] = [
  { icon: Layers, title: 'Group management', text: 'Run several groups at once, each with its own rules, members and status.' },
  { icon: BookOpenCheck, title: 'Contribution ledger', text: 'Every contribution by member and round, with who recorded it and whether the member confirmed it.' },
  { icon: CalendarClock, title: 'Rotation schedule', text: 'A fixed, versioned payout order that every member can see from day one.' },
  { icon: Link2, title: 'Member invites', text: 'Expiring, revocable invite links with capacity checks. No one joins without accepting the rules.' },
  { icon: FileBarChart, title: 'Reports and exports', text: 'Roster, contributions, outstanding amounts, payouts and disputes, with CSV download.' },
  { icon: History, title: 'Activity and audit trail', text: 'Records are never silently edited. Corrections are new entries linked to the original.' },
]

const faqs: { q: string; a: ReactNode }[] = [
  { q: 'What is ajo?', a: 'Ajo (also called esusu or adashe) is a savings group where members contribute a fixed amount on a regular schedule. In a rotating ajo, one member receives the pooled amount each round until everyone has had a turn.' },
  { q: 'Who can be an organizer?', a: 'Anyone who runs a savings group can create one after verifying their account. Organizers complete a stronger identity check than members, because members rely on them.' },
  { q: 'Can I manage more than one group?', a: 'Yes. Organizers can run several groups, and members can belong to more than one group. Each group keeps its own rules and records.' },
  { q: 'How are records confirmed?', a: 'The organizer records each contribution or payout. The member it concerns can confirm it, or raise a correction if it is wrong. A recorded entry is the organizer’s record, not bank confirmation.' },
  { q: `Does ${brand.name} hold our money?`, a: `No. ${brand.name} does not hold, collect, transfer or guarantee money. Members pay the way your group already pays; the app keeps the schedule and the records.` },
  { q: 'What happens if there is a dispute?', a: 'A member can dispute any record that concerns them. The organizer can correct it or explain it. If it is not settled within the time set by the group’s policy, it can be escalated for review, and every step is kept in the history.' },
]

/* ----------------------------------------------------------------------------------------------------------------
 * Building blocks
 * -------------------------------------------------------------------------------------------------------------- */

function Section({ id, eyebrow, title, intro, children, tone = 'white' }: { id?: string; eyebrow?: string; title: string; intro?: ReactNode; children: ReactNode; tone?: 'white' | 'canvas' }) {
  return (
    <section id={id} aria-labelledby={id ? `${id}-title` : undefined} className={`${tone === 'canvas' ? 'bg-paper' : 'bg-card'} py-14 sm:py-20`}>
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <div className="max-w-2xl">
          {eyebrow && <p className="text-xs font-semibold uppercase tracking-wider text-teal">{eyebrow}</p>}
          <h2 id={id ? `${id}-title` : undefined} className="mt-2 text-2xl font-semibold tracking-tight text-balance sm:text-3xl">{title}</h2>
          {intro && <p className="mt-3 text-base text-ink-soft sm:text-lg">{intro}</p>}
        </div>
        <div className="mt-8 sm:mt-10">{children}</div>
      </div>
    </section>
  )
}

function IconCard({ icon: Icon, title, text, index }: { icon: LucideIcon; title: string; text: string; index?: number }) {
  return (
    <div className="h-full rounded-[var(--radius-card)] border border-line bg-card p-5 shadow-[var(--shadow-card)]">
      <div className="flex items-center gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-lg bg-teal-soft text-teal"><Icon className="size-5" /></span>
        {index != null && <span className="text-xs font-semibold uppercase tracking-wider text-ink-soft">Step {index}</span>}
      </div>
      <h3 className="mt-4 font-semibold">{title}</h3>
      <p className="mt-1 text-sm leading-relaxed text-ink-soft">{text}</p>
    </div>
  )
}

function StepsGrid() {
  return (
    <ol className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {steps.map((s, i) => <li key={s.title}><IconCard {...s} index={i + 1} /></li>)}
    </ol>
  )
}

function FeatureGrid() {
  return (
    <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {features.map((f) => <li key={f.title}><IconCard {...f} /></li>)}
    </ul>
  )
}

function SchemeTypes() {
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <SchemeCard icon={Repeat} title="Rotating ajo" points={[
        'Everyone contributes the same amount every round.',
        'Each round, one member receives the pooled amount, in an order fixed before the group starts.',
        'The group ends when every member has had their turn.',
      ]} />
      <SchemeCard icon={PiggyBank} title="Fixed-term savings" points={[
        'Members contribute on a schedule towards a target over a set term.',
        'The split at the end is fixed in the rules before anyone joins.',
        'Leaving early or cancelling returns what was recorded as paid.',
      ]} />
      <p className="text-sm text-ink-soft md:col-span-2">Neither type earns interest or returns. {brand.name} tracks what members agreed to pay and what was recorded.</p>
    </div>
  )
}

function SchemeCard({ icon: Icon, title, points }: { icon: LucideIcon; title: string; points: string[] }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
      <div className="flex items-center gap-3">
        <span aria-hidden className="grid size-11 place-items-center rounded-lg bg-brand text-white"><Icon className="size-5" /></span>
        <h3 className="text-lg font-semibold">{title}</h3>
      </div>
      <ul className="mt-4 space-y-2.5">
        {points.map((p) => <li key={p} className="flex gap-2.5 text-sm text-ink-soft"><CheckCircle2 aria-hidden className="mt-0.5 size-4 shrink-0 text-teal" />{p}</li>)}
      </ul>
    </div>
  )
}

function Transparency() {
  const statuses: { icon: LucideIcon; label: string; text: string; cls: string }[] = [
    { icon: CircleDot, label: 'Scheduled', text: 'Due under the rules. Nothing recorded yet.', cls: 'bg-tone-neutral-bg text-tone-neutral' },
    { icon: BookOpenCheck, label: 'Recorded by organizer', text: 'The organizer says it was paid. Not bank-verified.', cls: 'bg-tone-info-bg text-tone-info' },
    { icon: CheckCircle2, label: 'Confirmed by member', text: 'The member agrees the record is right.', cls: 'bg-teal-soft text-teal' },
    { icon: AlertTriangle, label: 'Disputed', text: 'Someone says the record is wrong. It is being reviewed.', cls: 'bg-tone-warn-bg text-tone-warn' },
    { icon: History, label: 'Corrected', text: 'Replaced by a new linked entry. The original stays in history.', cls: 'bg-tone-neutral-bg text-tone-neutral' },
  ]
  return (
    <div className="grid gap-6 lg:grid-cols-[1.2fr_1fr] lg:items-start">
      <ul className="divide-y divide-line rounded-[var(--radius-card)] border border-line bg-card shadow-[var(--shadow-card)]">
        {statuses.map((s) => (
          <li key={s.label} className="flex flex-col gap-1.5 p-4 sm:flex-row sm:items-center sm:gap-4">
            <span className={`inline-flex w-fit shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold sm:w-52 ${s.cls}`}><s.icon aria-hidden className="size-3.5" />{s.label}</span>
            <span className="text-sm text-ink-soft">{s.text}</span>
          </li>
        ))}
      </ul>
      <div className="space-y-4 text-sm text-ink-soft">
        <p><strong className="text-ink">Rules are versioned.</strong> Every member accepts a specific saved version of the rules. Rules cannot change silently after a group starts.</p>
        <p><strong className="text-ink">History is permanent.</strong> Records are never edited or deleted. Corrections and reversals are added as new entries.</p>
        <p><strong className="text-ink">Tracking only.</strong> {brand.name} is a record-keeping tool. It is not a bank, wallet, escrow service or payment processor.</p>
      </div>
    </div>
  )
}

function Faqs() {
  return (
    <div className="mx-auto max-w-3xl divide-y divide-line rounded-[var(--radius-card)] border border-line bg-card shadow-[var(--shadow-card)]">
      {faqs.map((f) => (
        <details key={f.q} className="group">
          <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 px-5 py-3 font-medium [&::-webkit-details-marker]:hidden">
            {f.q}
            <ChevronDown aria-hidden className="size-5 shrink-0 text-ink-soft transition-transform group-open:rotate-180" />
          </summary>
          <p className="px-5 pb-5 text-sm leading-relaxed text-ink-soft">{f.a}</p>
        </details>
      ))}
    </div>
  )
}

function FinalCta({ title = 'Bring your ajo records into one place.', text = 'Create your first group in a few minutes. Members join with a link.' }: { title?: string; text?: string }) {
  return (
    <section className="bg-brand">
      <div className="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-14 sm:px-6 md:flex-row md:items-center md:justify-between">
        <div>
          <h2 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{title}</h2>
          <p className="mt-2 text-white/75">{text}</p>
        </div>
        <div className="flex flex-col gap-3 sm:flex-row">
          <Link to="/register" className="inline-flex min-h-12 items-center justify-center gap-2 rounded-lg bg-mint px-6 text-sm font-semibold text-brand hover:bg-white">Get Started<ArrowRight aria-hidden className="size-4" /></Link>
          <Link to="/how-it-works" className="inline-flex min-h-12 items-center justify-center rounded-lg border border-white/30 px-6 text-sm font-semibold text-white hover:bg-white/10">See how it works</Link>
        </div>
      </div>
    </section>
  )
}

/** Illustrative product preview built from markup (no screenshot), clearly labelled as example data. */
function ProductPreview() {
  const rows: { name: string; status: string; icon: LucideIcon; cls: string }[] = [
    { name: 'Chiamaka O.', status: 'Confirmed by member', icon: CheckCircle2, cls: 'bg-teal-soft text-teal' },
    { name: 'Ibrahim S.', status: 'Recorded by organizer', icon: BookOpenCheck, cls: 'bg-tone-info-bg text-tone-info' },
    { name: 'Funmi A.', status: 'Scheduled', icon: CircleDot, cls: 'bg-tone-neutral-bg text-tone-neutral' },
    { name: 'Emeka N.', status: 'Disputed', icon: AlertTriangle, cls: 'bg-tone-warn-bg text-tone-warn' },
  ]
  return (
    <figure className="relative mx-auto w-full max-w-md lg:max-w-none" aria-label="Example of a group page, with demo data">
      <div aria-hidden className="absolute -inset-4 -z-10 rounded-[2rem] bg-gradient-to-br from-teal-soft via-paper to-brand-soft" />
      <div className="overflow-hidden rounded-[var(--radius-card)] border border-line bg-card shadow-[var(--shadow-raised)]">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold">Market Women Monthly Ajo</p>
            <p className="text-xs text-ink-soft">Rotating ajo · ₦20,000 monthly</p>
          </div>
          <span className="shrink-0 rounded-full bg-amber/15 px-2 py-0.5 text-[11px] font-semibold text-tone-warn">Demo data</span>
        </div>
        <div className="grid grid-cols-3 divide-x divide-line border-b border-line text-center">
          {[['Round', '4 of 10'], ['Members', '10'], ['Next due', '5 Dec']].map(([k, v]) => (
            <div key={k} className="px-2 py-3"><p className="text-[11px] uppercase tracking-wide text-ink-soft">{k}</p><p className="mt-0.5 text-sm font-semibold tabular-nums">{v}</p></div>
          ))}
        </div>
        <div className="px-4 pt-3">
          <div className="flex justify-between text-xs text-ink-soft"><span>Round 4 recorded</span><span className="tabular-nums">₦140,000 of ₦200,000</span></div>
          <div className="mt-1.5 h-2 rounded-full bg-paper"><div className="h-2 w-[70%] rounded-full bg-teal" /></div>
        </div>
        <ul className="divide-y divide-line px-4 py-2">
          {rows.map((r) => (
            <li key={r.name} className="flex items-center justify-between gap-3 py-2.5">
              <span className="flex min-w-0 items-center gap-2.5">
                <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full bg-brand-soft text-[11px] font-bold text-brand">{r.name.split(' ').map((w) => w[0]).join('')}</span>
                <span className="truncate text-sm font-medium">{r.name}</span>
              </span>
              <span className={`inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold ${r.cls}`}><r.icon aria-hidden className="size-3" />{r.status}</span>
            </li>
          ))}
        </ul>
      </div>
      <figcaption className="sr-only">Example group page showing contribution statuses for round 4. All names and amounts are demo data.</figcaption>
    </figure>
  )
}

/* ----------------------------------------------------------------------------------------------------------------
 * Pages
 * -------------------------------------------------------------------------------------------------------------- */

export function HomePage() {
  usePageMeta('', 'Manage your ajo groups, contributions, schedules and member communication in one place. Tracking only — we never hold your money.')
  return (
    <>
      {/* Hero */}
      <section className="overflow-hidden bg-card">
        <div className="mx-auto grid max-w-6xl gap-12 px-4 pb-14 pt-10 sm:px-6 sm:pt-16 lg:grid-cols-[1.05fr_1fr] lg:items-center lg:gap-16 lg:pb-24">
          <div>
            <p className="inline-flex items-center gap-2 rounded-full border border-line bg-paper px-3 py-1 text-xs font-semibold text-ink-soft">
              <span aria-hidden className="size-1.5 rounded-full bg-mint" />Built for ajo, esusu and adashe groups
            </p>
            <h1 className="mt-5 text-4xl font-semibold leading-[1.1] tracking-tight text-balance sm:text-5xl">{brand.tagline}</h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-soft">
              Set up your groups, keep a clear record of every contribution, share the schedule with members and keep everyone informed — without the notebook and the endless WhatsApp messages.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <Link to="/register" className={`${ctaPrimary} min-h-12 px-6`}>Start managing your ajo<ArrowRight aria-hidden className="size-4" /></Link>
              <Link to="/how-it-works" className={`${ctaSecondary} min-h-12 px-6`}>See how it works</Link>
            </div>
            <p className="mt-5 flex items-center gap-2 text-sm text-ink-soft"><ShieldCheck aria-hidden className="size-4 text-teal" />We keep the records. Your money stays with your group.</p>
          </div>
          <ProductPreview />
        </div>
      </section>

      {/* Value strip */}
      <section aria-label="Why organizers use it" className="border-y border-line bg-paper">
        <ul className="mx-auto grid max-w-6xl gap-6 px-4 py-8 sm:grid-cols-3 sm:px-6">
          {[
            [BookOpenCheck, 'Organized records', 'Every contribution and payout in one ledger.'],
            [CalendarClock, 'Transparent schedules', 'The same dates and payout order for every member.'],
            [Layers, 'Multi-group management', 'Run all your groups from one workspace.'],
          ].map(([Icon, t, d]) => {
            const I = Icon as LucideIcon
            return (
              <li key={t as string} className="flex gap-3">
                <I aria-hidden className="mt-0.5 size-5 shrink-0 text-teal" />
                <div><p className="font-semibold">{t as string}</p><p className="text-sm text-ink-soft">{d as string}</p></div>
              </li>
            )
          })}
        </ul>
      </section>

      <Section id="how" eyebrow="How it works" title="From first meeting to final payout" intro="Four steps, and every member sees the same thing.">
        <StepsGrid />
      </Section>
      <Section id="features" eyebrow="Features" title="Everything an organizer keeps track of" tone="canvas">
        <FeatureGrid />
      </Section>
      <Section id="types" eyebrow="Savings types" title="Two ways to save together" intro="Pick the type when you create a group. The rules are fixed before anyone joins.">
        <SchemeTypes />
      </Section>
      <Section id="organizers" eyebrow="For organizers" title="Spend less time on bookkeeping" tone="canvas">
        <OrganizerBenefits />
        <Link to="/organizers" className="mt-6 inline-flex min-h-11 items-center gap-1.5 text-sm font-semibold text-teal hover:text-teal-dark">More for organizers<ArrowRight aria-hidden className="size-4" /></Link>
      </Section>
      <Section id="transparency" eyebrow="Transparency" title="Clear about what a record means" intro="Every record shows exactly what is known about it, in words and with an icon, not just a colour.">
        <Transparency />
      </Section>
      <Section id="pricing" eyebrow="Pricing" title="Pricing" tone="canvas">
        <PricingBody compact />
      </Section>
      <Section id="faqs" eyebrow="FAQs" title="Questions organizers ask">
        <Faqs />
      </Section>
      <FinalCta />
    </>
  )
}

function OrganizerBenefits() {
  const items: { icon: LucideIcon; title: string; text: string }[] = [
    { icon: Layers, title: 'Manage multiple groups', text: 'See every group you run, what is due and what needs attention, on one dashboard.' },
    { icon: ClipboardList, title: 'Reduce manual bookkeeping', text: 'Schedules and totals are calculated for you. Record a payment in seconds.' },
    { icon: MessageSquare, title: 'Communicate with members', text: 'Members get reminders and updates, and can see their own records any time.' },
    { icon: FileBarChart, title: 'Access records any time', text: 'Download reports when a member asks, or at the end of a cycle.' },
  ]
  return <ul className="grid gap-4 sm:grid-cols-2">{items.map((i) => <li key={i.title}><IconCard {...i} /></li>)}</ul>
}

function PricingBody({ compact }: { compact?: boolean }) {
  const p = brand.pricing
  if (p.announced && p.plans.length) {
    return (
      <ul className="grid gap-4 md:grid-cols-3">
        {p.plans.map((plan) => (
          <li key={plan.name} className="rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
            <h3 className="font-semibold">{plan.name}</h3>
            <p className="mt-2 text-3xl font-semibold tabular-nums">{plan.price}</p>
            <p className="mt-2 text-sm text-ink-soft">{plan.description}</p>
            <ul className="mt-4 space-y-2">{plan.features.map((f) => <li key={f} className="flex gap-2 text-sm"><CheckCircle2 aria-hidden className="mt-0.5 size-4 shrink-0 text-teal" />{f}</li>)}</ul>
          </li>
        ))}
      </ul>
    )
  }
  return (
    <div className="max-w-2xl rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
      <p className="text-ink-soft">{p.note}</p>
      <div className="mt-5 flex flex-col gap-3 sm:flex-row">
        <Link to="/register" className={ctaPrimary}>Create an account</Link>
        {compact ? <Link to="/pricing" className={ctaSecondary}>Pricing details</Link> : <Link to="/contact" className={ctaSecondary}>Ask about pricing</Link>}
      </div>
    </div>
  )
}

function PageHero({ eyebrow, title, intro }: { eyebrow: string; title: string; intro: string }) {
  return (
    <section className="border-b border-line bg-paper">
      <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6 sm:py-16">
        <p className="text-xs font-semibold uppercase tracking-wider text-teal">{eyebrow}</p>
        <h1 className="mt-2 max-w-3xl text-3xl font-semibold tracking-tight text-balance sm:text-4xl">{title}</h1>
        <p className="mt-4 max-w-2xl text-lg text-ink-soft">{intro}</p>
      </div>
    </section>
  )
}

export function HowItWorksPage() {
  usePageMeta('How It Works', 'Create a group, set the rules and schedule, invite members and track records together.')
  return (
    <>
      <PageHero eyebrow="How it works" title="Four steps from setup to a shared record" intro="The organizer sets the group up once. After that, everyone works from the same rules, dates and records." />
      <Section title="The steps"><StepsGrid /></Section>
      <Section title="Two savings types" tone="canvas"><SchemeTypes /></Section>
      <Section title="What members see"><Transparency /></Section>
      <FinalCta />
    </>
  )
}

export function FeaturesPage() {
  usePageMeta('Features', 'Group management, contribution ledger, rotation schedule, invites, reports and an audit trail for ajo organizers.')
  return (
    <>
      <PageHero eyebrow="Features" title="Tools for running savings groups properly" intro="Built around how ajo groups actually work in Nigeria: fixed amounts, fixed turns, and an organizer everyone trusts." />
      <Section title="What’s included"><FeatureGrid /></Section>
      <Section title="Questions" tone="canvas"><Faqs /></Section>
      <FinalCta />
    </>
  )
}

export function OrganizersPage() {
  usePageMeta('For Organizers', 'Manage multiple ajo groups, reduce bookkeeping, communicate with members and keep clear records.')
  return (
    <>
      <PageHero eyebrow="For organizers" title="Run your groups with less work and fewer arguments" intro="Members can see the rules, their schedule and their own records any time, so you answer fewer questions." />
      <Section title="What changes for you"><OrganizerBenefits /></Section>
      <Section title="What it takes to get started" tone="canvas">
        <ul className="grid gap-4 md:grid-cols-3">
          {[
            [UserPlus, 'Create your account', 'Sign up with your email and verify it with a code.'],
            [ShieldCheck, 'Verify your identity', 'Organizers confirm their phone number, an ID document, BVN and a passport photo. Members rely on you, so this protects them.'],
            [Layers, 'Create your first group', 'Pick a savings type, set the rules, review the schedule and publish. Then share the invite link.'],
          ].map(([I, t, d]) => <li key={t as string}><IconCard icon={I as LucideIcon} title={t as string} text={d as string} /></li>)}
        </ul>
      </Section>
      <FinalCta title="Ready to set up your first group?" />
    </>
  )
}

export function PricingPage() {
  usePageMeta('Pricing', `Pricing for ${brand.name}.`)
  return (
    <>
      <PageHero eyebrow="Pricing" title="Pricing" intro="Clear pricing, published before anyone is charged." />
      <Section title="Current status"><PricingBody /></Section>
      <FinalCta />
    </>
  )
}

export function AboutPage() {
  usePageMeta('About', `${brand.name} helps independent ajo organizers keep clear, shared records for their savings groups.`)
  return (
    <>
      <PageHero eyebrow="About" title="Built for the people who keep ajo running" intro={`Ajo groups run on trust. ${brand.name} gives organizers and members a shared, permanent record so that trust is easier to keep.`} />
      <Section title="What we do and don’t do">
        <div className="grid gap-4 md:grid-cols-2">
          <ListCard icon={CheckCircle2} title="We do" items={['Keep each group’s rules, schedule and members in one place.', 'Record contributions and payouts with a permanent history.', 'Let members confirm or dispute records that concern them.', 'Send reminders and updates.']} />
          <ListCard icon={Scale} title="We don’t" items={['Hold, collect, pool or transfer money.', 'Guarantee payouts or promise returns.', 'Act as a bank, wallet or escrow service.', 'Share members’ contact details with other members.']} />
        </div>
      </Section>
      <FinalCta />
    </>
  )
}

function ListCard({ icon: Icon, title, items }: { icon: LucideIcon; title: string; items: string[] }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
      <h3 className="font-semibold">{title}</h3>
      <ul className="mt-3 space-y-2">{items.map((i) => <li key={i} className="flex gap-2.5 text-sm text-ink-soft"><Icon aria-hidden className="mt-0.5 size-4 shrink-0 text-teal" />{i}</li>)}</ul>
    </div>
  )
}

export function ContactPage() {
  usePageMeta('Contact', `Contact the ${brand.name} team.`)
  return (
    <>
      <PageHero eyebrow="Contact" title="Get in touch" intro="Questions about the product, pricing or a group you belong to." />
      <Section title="How to reach us">
        <div className="grid gap-4 md:grid-cols-2">
          <div className="rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
            <Mail aria-hidden className="size-6 text-teal" />
            <h3 className="mt-3 font-semibold">Email</h3>
            <p className="mt-1 text-sm text-ink-soft">We reply on working days.</p>
            <a href={`mailto:${brand.supportEmail}`} className="mt-3 inline-flex min-h-11 items-center font-semibold text-teal underline-offset-4 hover:underline">{brand.supportEmail}</a>
          </div>
          <div className="rounded-[var(--radius-card)] border border-line bg-card p-6 shadow-[var(--shadow-card)]">
            <Users aria-hidden className="size-6 text-teal" />
            <h3 className="mt-3 font-semibold">About a record in your group</h3>
            <p className="mt-1 text-sm text-ink-soft">If a contribution or payout record looks wrong, raise a dispute on that record in the app. It reaches your organizer and is kept in the group’s history.</p>
            <Link to="/login" className="mt-3 inline-flex min-h-11 items-center font-semibold text-teal underline-offset-4 hover:underline">Log in to your group</Link>
          </div>
        </div>
        <p className="mt-6 text-sm text-ink-soft">Never send passwords, verification codes, BVN or card details by email. We will never ask for them.</p>
      </Section>
    </>
  )
}

type LegalKind = 'privacy' | 'terms' | 'disclaimer'
const legal: Record<LegalKind, { title: string; intro: string; points: string[] }> = {
  privacy: {
    title: 'Privacy Notice',
    intro: 'How we handle personal information. This is a draft summary.',
    points: [
      'We collect only what the service needs: your name, email, phone number and, for identity verification, the documents you submit.',
      'Identity documents are encrypted and deleted after review, within the retention period set in our policy.',
      'Members of a group see each other’s names, not contact details. Organizers see only their own groups.',
      'Every staff view of identity documents is recorded in an audit log.',
      'You can ask for a copy of your data or for it to be deleted, subject to record-keeping duties that apply to group records.',
    ],
  },
  terms: {
    title: 'Terms of Service',
    intro: 'The rules for using the service. This is a draft summary.',
    points: [
      `${brand.name} is a record-keeping tool. Groups decide among themselves how money moves.`,
      'Organizers are responsible for the accuracy of the records they enter and for the money they hold for their groups.',
      'Rules accepted by members cannot be changed silently. Changes need member notice and acceptance.',
      'Records are permanent. Mistakes are fixed with linked corrections, not edits.',
      'Accounts may be suspended for fraud, abuse or breaking these terms.',
    ],
  },
  disclaimer: {
    title: 'Financial services disclaimer',
    intro: `What ${brand.name} is, and what it is not.`,
    points: [
      `${brand.name} is not a bank, microfinance bank, payment service provider, wallet, escrow service or investment platform.`,
      'We do not hold, collect, pool, transfer, safeguard or guarantee money.',
      'A “scheduled” payout is a plan, not a promise of payment. A “recorded” contribution is the organizer’s record, not bank confirmation.',
      'Savings groups on the app do not earn interest or returns, and none are promised.',
      'Any future payment feature would only be offered through an appropriately licensed partner, and would be clearly labelled.',
    ],
  },
}

export function LegalPage({ kind }: { kind: LegalKind }) {
  const doc = legal[kind]
  usePageMeta(doc.title, doc.intro)
  return (
    <article className="mx-auto max-w-3xl px-4 py-12 sm:px-6 sm:py-16">
      <FileText aria-hidden className="size-8 text-teal" />
      <h1 className="mt-3 text-3xl font-semibold tracking-tight">{doc.title}</h1>
      <p className="mt-2 text-lg text-ink-soft">{doc.intro}</p>
      {!brand.legalReviewed && (
        <div className="mt-6"><Alert tone="warn" title="Draft — pending legal review">
          This text is a placeholder. It must be reviewed by qualified Nigerian legal counsel, including for Nigeria Data Protection Act requirements, before a public launch.
        </Alert></div>
      )}
      <ul className="mt-8 space-y-4">
        {doc.points.map((p) => <li key={p} className="flex gap-3 leading-relaxed"><CheckCircle2 aria-hidden className="mt-1 size-4 shrink-0 text-teal" />{p}</li>)}
      </ul>
      <p className="mt-10 text-sm text-ink-soft">Questions? Email <a href={`mailto:${brand.supportEmail}`} className="font-semibold text-teal underline-offset-4 hover:underline">{brand.supportEmail}</a>.</p>
    </article>
  )
}

/* ----------------------------------------------------------------------------------------------------------------
 * System pages
 * -------------------------------------------------------------------------------------------------------------- */

export function SystemMessage({ code, title, text, action }: { code: string; title: string; text: string; action?: ReactNode }) {
  usePageMeta(title)
  return (
    <div className="mx-auto flex max-w-lg flex-col items-center px-4 py-20 text-center">
      <p className="text-sm font-semibold tabular-nums text-teal">{code}</p>
      <h1 className="mt-2 text-3xl font-semibold tracking-tight">{title}</h1>
      <p className="mt-3 text-ink-soft">{text}</p>
      <div className="mt-8 flex flex-col gap-3 sm:flex-row">
        {action ?? <Link to="/" className={ctaPrimary}>Go to the home page</Link>}
      </div>
    </div>
  )
}

export function NotFoundPage() {
  return <SystemMessage code="404" title="Page not found" text="The page you’re looking for doesn’t exist or has moved." />
}

export function ForbiddenPage() {
  return <SystemMessage code="403" title="You don’t have access to this page" text="Your account doesn’t have permission to see this. If you think it should, ask the group organizer or contact support."
    action={<><Link to="/app/dashboard" className={ctaPrimary}>Go to your dashboard</Link><Link to="/contact" className={ctaSecondary}>Contact support</Link></>} />
}
