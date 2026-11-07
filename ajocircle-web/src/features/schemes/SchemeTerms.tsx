import { Stat } from '../../components/ui'
import { formatDate, formatMoney, frequencyLabel, frequencyNoun } from '../../lib/format'
import type { FixedTermRules, FixedTermSummary, RoundPlan, RotatingRules, SchemeSummary } from '../../types/api'
import { allocationLabel } from './labels'

function TermsList({ rows }: { rows: [string, string][] }) {
  return (
    <dl className="divide-y divide-line rounded-lg border border-line">
      {rows.map(([k, v]) => (
        <div key={k} className="grid grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-3 px-3 py-2.5 text-sm">
          <dt className="text-ink-soft">{k}</dt>
          <dd className="font-medium">{v}</dd>
        </div>
      ))}
    </dl>
  )
}

/** The member-facing terms. Shown identically in the wizard preview, group page and invitation page. */
export function SchemeTerms({ rules, summary }: { rules: RotatingRules; summary: SchemeSummary }) {
  const rows: [string, string][] = [
    ['Scheme', 'Rotating ajo — each member receives one payout'],
    ['Contribution', `${formatMoney(rules.contributionAmountMinor)} per member, ${frequencyLabel[rules.frequency].toLowerCase()}`],
    ['Members', `${rules.memberCount} (exactly — the group starts only when full)`],
    ['Rounds', `${summary.rounds} — one ${frequencyNoun[rules.frequency]} per round`],
    ['First contribution due', formatDate(summary.firstDueDate)],
    ['Payout timing', rules.payoutOffsetDays === 0 ? 'Same day contributions are due' : `${rules.payoutOffsetDays} day(s) after contributions are due`],
    ['Payout order', rules.payoutOrder === 'JoinOrder' ? 'Order in which members join' : 'Assigned by the organizer before the group starts'],
    ['Final payout', formatDate(summary.finalPayoutDate)],
    ['Fees', 'None'],
  ]
  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Stat label="You pay each round" value={formatMoney(summary.contributionPerMemberPerRound)} />
        <Stat label="Your total commitment" value={formatMoney(summary.totalCommitmentPerMember)} note={`over ${summary.rounds} rounds`} />
        <Stat label="Scheduled pool per round" value={formatMoney(summary.expectedPoolPerRound)} note="paid to that round’s recipient" />
      </dl>
      <TermsList rows={rows} />
      <p className="text-xs text-ink-soft">
        All amounts are scheduled commitments, not money collected or held. Payments happen outside this app; a scheduled payout is not guaranteed.
      </p>
    </div>
  )
}

/**
 * Fixed-term savings terms, including the owner-set rules on leaving early and cancellation (shown, not configurable).
 * The organizer holds the money; the app only records what is paid and what is owed back.
 */
export function FixedTermTerms({ rules, summary }: { rules: FixedTermRules; summary: FixedTermSummary }) {
  const shared = rules.variant === 'Shared'
  const rows: [string, string][] = [
    ['Scheme', shared ? 'Shared savings — members save towards one goal' : 'Individual savings — each member saves towards their own target'],
    ...(rules.goal ? [['Goal', rules.goal] as [string, string]] : []),
    ['Contribution', `${formatMoney(rules.contributionAmountMinor)} per member, ${frequencyLabel[rules.frequency].toLowerCase()}`],
    ['Number of contributions', `${rules.periods} (${formatDate(summary.firstDueDate)} to ${formatDate(summary.lastDueDate)})`],
    ['Members', `Up to ${rules.memberCount} — can start once at least 2 have joined`],
    ['Money released', `${formatDate(summary.releaseDate)}${rules.releaseOffsetDays ? ` (${rules.releaseOffsetDays} day(s) after the last contribution)` : ' (the day of the last contribution)'}`],
    ['How it’s shared', allocationLabel[rules.allocation]],
    ['Leaving early', 'You get back what you paid. Contributions not yet due are dropped.'],
    ['If the group is cancelled', 'Every member gets back what they paid.'],
    ['Who holds the money', 'The organizer. This app records payments; it never holds or moves money.'],
    ['Part payments', 'Not accepted — each contribution is recorded only when paid in full.'],
    ['Fees', 'None'],
  ]
  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Stat label="You pay each time" value={formatMoney(summary.contributionPerPeriod)} />
        <Stat label="Your target" value={formatMoney(summary.targetPerMember)} note={summary.periods === 1 ? "one contribution" : `over ${summary.periods} contributions`} />
        <Stat label="Released on" value={formatDate(summary.releaseDate)} note="under the sharing rule" />
      </dl>
      <TermsList rows={rows} />
      <p className="text-xs text-ink-soft">
        Targets are scheduled commitments, not money collected or held. Reaching a target doesn’t mean the money is available — the organizer pays it out and records it here.
      </p>
    </div>
  )
}

/** Picks the right terms view for whichever scheme the group uses. */
export function GroupTerms({ rules, summary, fixedTermRules, fixedTermSummary }: {
  rules: RotatingRules | null; summary: SchemeSummary | null; fixedTermRules: FixedTermRules | null; fixedTermSummary: FixedTermSummary | null
}) {
  if (fixedTermRules && fixedTermSummary) return <FixedTermTerms rules={fixedTermRules} summary={fixedTermSummary} />
  if (rules && summary) return <SchemeTerms rules={rules} summary={summary} />
  return null
}

export function ScheduleTable({ schedule, recipients }: { schedule: RoundPlan[]; recipients?: Record<number, string | null> }) {
  const hasPayouts = schedule.some((r) => r.payoutDate)
  return (
    <div className="overflow-x-auto rounded-lg border border-line">
      <table className={`w-full text-sm ${hasPayouts ? 'min-w-[420px]' : ''}`}>
        <thead className="bg-paper text-left text-xs uppercase tracking-wider text-ink-soft">
          <tr>
            <th scope="col" className="px-3 py-2">{hasPayouts ? 'Round' : 'Contribution'}</th>
            <th scope="col" className="px-3 py-2">{hasPayouts ? 'Contributions due' : 'Due'}</th>
            {hasPayouts && <th scope="col" className="px-3 py-2">Payout date</th>}
            {recipients && <th scope="col" className="px-3 py-2">Recipient</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {schedule.map((r) => (
            <tr key={r.roundNumber}>
              <td className="px-3 py-2 font-semibold tabular-nums">{r.roundNumber}</td>
              <td className="px-3 py-2">{formatDate(r.contributionDueDate)}</td>
              {hasPayouts && <td className="px-3 py-2">{r.payoutDate && formatDate(r.payoutDate)}</td>}
              {recipients && <td className="px-3 py-2">{recipients[r.roundNumber] ?? <span className="text-ink-soft">Not yet assigned</span>}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
