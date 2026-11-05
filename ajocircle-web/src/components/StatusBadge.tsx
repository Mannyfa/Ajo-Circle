import type { GroupStatus, ObligationStatus, PayoutStatus } from '../types/api'

type Tone = 'neutral' | 'info' | 'warn' | 'danger' | 'verified'
const toneCls: Record<Tone, string> = {
  neutral: 'bg-tone-neutral-bg text-tone-neutral',
  info: 'bg-tone-info-bg text-tone-info',
  warn: 'bg-tone-warn-bg text-tone-warn',
  danger: 'bg-tone-danger-bg text-tone-danger',
  verified: 'bg-tone-verified-bg text-tone-verified',
}

/** Each badge pairs a shape glyph with text — status is never conveyed by colour alone (brief §5.6). */
function Badge({ tone, glyph, label, title }: { tone: Tone; glyph: string; label: string; title?: string }) {
  return (
    <span title={title} className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold ${toneCls[tone]}`}>
      <span aria-hidden="true">{glyph}</span>
      {label}
    </span>
  )
}

const groupMap: Record<GroupStatus, [Tone, string, string]> = {
  Draft: ['neutral', '✎', 'Draft'],
  OpenForJoining: ['info', '◌', 'Open for joining'],
  ReadyToStart: ['warn', '◑', 'Ready to start'],
  Active: ['info', '●', 'Active'],
  Suspended: ['danger', '❚❚', 'Suspended'],
  Completed: ['neutral', '■', 'Completed'],
  Cancelled: ['danger', '✕', 'Cancelled'],
}

export function GroupStatusBadge({ status }: { status: GroupStatus }) {
  const [tone, glyph, label] = groupMap[status]
  return <Badge tone={tone} glyph={glyph} label={label} />
}

/**
 * Contribution states are deliberately distinct (brief Rule 2): an obligation is not money received,
 * an organizer record is not verification, member confirmation is not bank settlement.
 * Only provider-verified uses the "verified" (green) tone.
 */
const obligationMap: Record<ObligationStatus, [Tone, string, string, string]> = {
  Due: ['neutral', '○', 'Due', 'Scheduled obligation — no payment has been recorded.'],
  SubmittedForReview: ['warn', '◔', 'Submitted for review', 'A record has been submitted but is not final.'],
  RecordedByOrganizer: ['info', '✎', 'Recorded by organizer', 'The organizer reports this was paid outside the app. Not independently verified.'],
  ConfirmedByMember: ['info', '✔', 'Confirmed by member', 'The member confirmed the organizer’s record. Not proof of bank settlement.'],
  VerifiedByProvider: ['verified', '✔✔', 'Verified by provider', 'Verified by an approved payment provider.'],
  Disputed: ['danger', '!', 'Disputed', 'Someone has raised an unresolved issue.'],
  Reversed: ['danger', '↺', 'Reversed', 'A previous record was reversed through a traceable correction.'],
}

export function ObligationStatusBadge({ status }: { status: ObligationStatus }) {
  const [tone, glyph, label, title] = obligationMap[status]
  return <Badge tone={tone} glyph={glyph} label={label} title={title} />
}

export function PayoutStatusBadge({ status }: { status: PayoutStatus }) {
  return status === 'Scheduled'
    ? <Badge tone="neutral" glyph="◷" label="Scheduled" title="A scheduled payout is not funded, executed or guaranteed." />
    : null
}
