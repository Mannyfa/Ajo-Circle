import { Stat } from '../../components/ui'
import { formatDate, formatMoney, frequencyLabel, frequencyNoun } from '../../lib/format'
import type { RoundPlan, RotatingRules, SchemeSummary } from '../../types/api'

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
      <dl className="divide-y divide-line rounded-xl border border-line">
        {rows.map(([k, v]) => (
          <div key={k} className="grid grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-3 px-3 py-2.5 text-sm">
            <dt className="text-ink-soft">{k}</dt>
            <dd className="font-medium">{v}</dd>
          </div>
        ))}
      </dl>
      <p className="text-xs text-ink-soft">
        All amounts are scheduled commitments, not money collected or held. Payments happen outside this app; a scheduled payout is not guaranteed.
      </p>
    </div>
  )
}

export function ScheduleTable({ schedule, recipients }: { schedule: RoundPlan[]; recipients?: Record<number, string | null> }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-line">
      <table className="w-full min-w-[420px] text-sm">
        <thead className="bg-paper text-left text-xs uppercase tracking-wider text-ink-soft">
          <tr>
            <th scope="col" className="px-3 py-2">Round</th>
            <th scope="col" className="px-3 py-2">Contributions due</th>
            <th scope="col" className="px-3 py-2">Payout date</th>
            {recipients && <th scope="col" className="px-3 py-2">Recipient</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {schedule.map((r) => (
            <tr key={r.roundNumber}>
              <td className="px-3 py-2 font-semibold tabular-nums">{r.roundNumber}</td>
              <td className="px-3 py-2">{formatDate(r.contributionDueDate)}</td>
              <td className="px-3 py-2">{formatDate(r.payoutDate)}</td>
              {recipients && <td className="px-3 py-2">{recipients[r.roundNumber] ?? <span className="text-ink-soft">Not yet assigned</span>}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
