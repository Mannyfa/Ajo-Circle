import {
  AlertTriangle, Ban, BadgeCheck, Check, CheckCheck, CircleDashed, Clock, Flag, Lock, Minus, PauseCircle, PenLine,
  PlayCircle, RotateCcw, Square, UserPlus, type LucideIcon,
} from 'lucide-react'
import type { DisputeStatus, GroupStatus, ObligationStatus, PayoutStatus, RecordStatus } from '../types/api'

type Tone = 'neutral' | 'info' | 'warn' | 'danger' | 'verified'
const toneCls: Record<Tone, string> = {
  neutral: 'bg-tone-neutral-bg text-tone-neutral ring-tone-neutral/15',
  info: 'bg-tone-info-bg text-tone-info ring-tone-info/20',
  warn: 'bg-tone-warn-bg text-tone-warn ring-tone-warn/20',
  danger: 'bg-tone-danger-bg text-tone-danger ring-tone-danger/20',
  verified: 'bg-tone-verified-bg text-tone-verified ring-tone-verified/20',
}

/** Each badge pairs an icon with text — status is never conveyed by colour alone (brief §5.6). */
function Badge({ tone, icon: Icon, label, title }: { tone: Tone; icon: LucideIcon; label: string; title?: string }) {
  return (
    <span title={title} className={`inline-flex items-center gap-1 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-semibold ring-1 ring-inset ${toneCls[tone]}`}>
      <Icon aria-hidden className="size-3.5" strokeWidth={2.25} />
      {label}
    </span>
  )
}

type Spec = [Tone, LucideIcon, string, string?]

const groupMap: Record<GroupStatus, Spec> = {
  Draft: ['neutral', PenLine, 'Draft'],
  OpenForJoining: ['info', UserPlus, 'Open for joining'],
  ReadyToStart: ['warn', Clock, 'Ready to start'],
  Active: ['info', PlayCircle, 'Active'],
  Suspended: ['danger', PauseCircle, 'Suspended'],
  Completed: ['neutral', Square, 'Completed'],
  Cancelled: ['danger', Ban, 'Cancelled'],
}

export function GroupStatusBadge({ status }: { status: GroupStatus }) {
  const [tone, icon, label] = groupMap[status]
  return <Badge tone={tone} icon={icon} label={label} />
}

/**
 * Contribution states are deliberately distinct (brief Rule 2): an obligation is not money received,
 * an organizer record is not verification, member confirmation is not bank settlement.
 * Only provider-verified uses the "verified" (green) tone.
 */
const obligationMap: Record<ObligationStatus, Spec> = {
  Due: ['neutral', CircleDashed, 'Due', 'Scheduled obligation — no payment has been recorded.'],
  SubmittedForReview: ['warn', Clock, 'Submitted for review', 'A record has been submitted but is not final.'],
  RecordedByOrganizer: ['info', PenLine, 'Recorded by organizer', 'The organizer reports this was paid outside the app. Not independently verified.'],
  ConfirmedByMember: ['info', Check, 'Confirmed by member', 'The member confirmed the organizer’s record. Not proof of bank settlement.'],
  VerifiedByProvider: ['verified', BadgeCheck, 'Verified by provider', 'Verified by an approved payment provider.'],
  Disputed: ['danger', AlertTriangle, 'Disputed', 'Someone has raised an unresolved issue.'],
  Reversed: ['danger', RotateCcw, 'Reversed', 'A previous record was reversed through a traceable correction.'],
  Cancelled: ['neutral', Minus, 'No longer owed', 'Not owed: the member left early or the group was cancelled before it was due.'],
}

export function ObligationStatusBadge({ status }: { status: ObligationStatus }) {
  const [tone, icon, label, title] = obligationMap[status]
  return <Badge tone={tone} icon={icon} label={label} title={title} />
}

const payoutMap: Record<PayoutStatus, Spec> = {
  Scheduled: ['neutral', Clock, 'Scheduled', 'A scheduled payout is not funded, executed or guaranteed.'],
  RecordedByOrganizer: ['info', PenLine, 'Recorded by organizer', 'The organizer reports this payout was made outside the app. Not independently verified.'],
  ConfirmedByRecipient: ['info', CheckCheck, 'Confirmed by recipient', 'The recipient confirmed receiving it. Not proof of bank settlement.'],
  Disputed: ['danger', AlertTriangle, 'Disputed', 'The recipient has raised an unresolved issue.'],
}

export function PayoutStatusBadge({ status }: { status: PayoutStatus }) {
  const [tone, icon, label, title] = payoutMap[status]
  return <Badge tone={tone} icon={icon} label={label} title={title} />
}

/** For a record on a payout, "confirmed by member" means the recipient confirmed. */
export function RecordStatusBadge({ status, payout }: { status: RecordStatus; payout?: boolean }) {
  if (status === 'ConfirmedByMember' && payout) return <PayoutStatusBadge status="ConfirmedByRecipient" />
  return <ObligationStatusBadge status={status} />
}

export function DisputeStatusBadge({ status }: { status: DisputeStatus }) {
  if (status === 'Open') return <Badge tone="warn" icon={AlertTriangle} label="Open" title="Waiting for the organizer to correct the record or the member to withdraw." />
  if (status === 'Escalated') return <Badge tone="danger" icon={Flag} label="With operations" title="The operations team is investigating." />
  return <Badge tone="neutral" icon={Lock} label="Resolved" />
}
