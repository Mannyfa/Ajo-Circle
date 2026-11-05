import { Link } from 'react-router-dom'
import { brand } from '../app/brand'
import { Alert, Card } from '../components/ui'

export function LandingPage() {
  return (
    <div className="mx-auto max-w-5xl px-4">
      <section className="py-10 sm:py-16">
        <p className="mb-3 inline-block rounded-full bg-brand-soft px-3 py-1 text-xs font-bold uppercase tracking-wider text-brand-dark">For ajo organizers</p>
        <h1 className="max-w-2xl text-4xl font-extrabold leading-tight tracking-tight sm:text-5xl">
          Every member sees the same rules, dates and records.
        </h1>
        <p className="mt-4 max-w-xl text-lg text-ink-soft">
          {brand.tagline} Set up rotating ajo groups, invite members with a link, and keep a clear history of who owes what and when.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link to="/register" className="rounded-xl bg-brand px-5 py-3 font-semibold text-white hover:bg-brand-dark">Create your first group</Link>
          <a href="#how" className="rounded-xl border border-line bg-card px-5 py-3 font-semibold">How it works</a>
        </div>
      </section>

      <section id="how" className="grid gap-4 pb-12 sm:grid-cols-3">
        {[
          ['1', 'Set the terms', 'Contribution, frequency, number of members, start date and payout order. We calculate the totals and schedule.'],
          ['2', 'Invite by link', 'Members open a secure link, review the exact terms and accept them before joining. Accepted terms are saved.'],
          ['3', 'Track together', 'When the group starts, each round’s contributions and payout recipient are scheduled for everyone to see.'],
        ].map(([n, t, d]) => (
          <Card key={n}>
            <span className="grid size-8 place-items-center rounded-full bg-ink text-sm font-bold text-white">{n}</span>
            <h2 className="mt-3 font-bold">{t}</h2>
            <p className="mt-1 text-sm text-ink-soft">{d}</p>
          </Card>
        ))}
      </section>

      <section className="pb-16">
        <Alert tone="info" title={`${brand.name} does not hold your money`}>
          Contributions and payouts happen outside the app, the way your group already pays. {brand.name} keeps the schedule and the records.
          Records entered by an organizer are labelled as such and are not bank or provider verification.
        </Alert>
      </section>
    </div>
  )
}

export function TrustPage() {
  return (
    <article className="mx-auto max-w-2xl space-y-4 px-4 py-10">
      <h1 className="text-3xl font-extrabold">Trust & Safety</h1>
      <p>{brand.name} is a record-keeping tool for savings groups. It is not a bank, wallet, escrow service or payment processor.</p>
      <ul className="list-disc space-y-2 pl-5 text-ink-soft">
        <li>We do not collect, hold, pool or transfer member funds. Scheduled amounts are obligations, not money received.</li>
        <li>Payouts shown in the app are schedules. A scheduled payout is not funded or guaranteed.</li>
        <li>Each member accepts a specific, saved version of the group’s terms. Terms cannot be changed silently after publishing.</li>
        <li>Records are never silently edited or deleted. Corrections are added as new, linked entries.</li>
        <li>Organizers can only see their own groups. Members see the group’s rules and schedule, not other members’ contact details.</li>
      </ul>
    </article>
  )
}

export function PolicyPlaceholder({ title }: { title: string }) {
  return (
    <article className="mx-auto max-w-2xl space-y-4 px-4 py-10">
      <h1 className="text-3xl font-extrabold">{title}</h1>
      <Alert tone="warn" title="Draft — pending legal review">
        This document has not been finalized. It must be reviewed by qualified Nigerian legal counsel (including NDPA data-protection requirements) before a public pilot.
      </Alert>
    </article>
  )
}
