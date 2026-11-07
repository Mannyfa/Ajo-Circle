import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PayoutStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, EmptyState, ErrorAlert, Field, Loading, Stat, TextInput } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatMoney } from '../../lib/format'
import type { GroupDetail, Member, PayoutListItem, SavingsProgress } from '../../types/api'
import { formatShare, payoutKindLabel } from '../schemes/labels'

/** Brief §7.3 figures, kept apart. "Recorded" is the organizer's records — never shown as verified money. */
export function SavingsProgressCard({ s }: { s: SavingsProgress }) {
  const pct = s.target.amountMinor ? Math.min(100, Math.round((s.recorded.amountMinor / s.target.amountMinor) * 100)) : 0
  return (
    <Card className="space-y-3">
      <h2 className="font-bold">{s.forYou ? 'Your savings' : `Group savings · ${s.members} member${s.members === 1 ? '' : 's'}`}</h2>
      <div>
        <div className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
          <span>Recorded by the organizer</span>
          <span className="tabular-nums"><strong>{formatMoney(s.recorded)}</strong> <span className="text-ink-soft">of {formatMoney(s.target)} target</span></span>
        </div>
        <div className="mt-1 h-2 rounded-full bg-line" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label="Recorded towards target">
          <div className="h-2 rounded-full bg-teal" style={{ width: `${pct}%` }} />
        </div>
      </div>
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat label="Scheduled" value={formatMoney(s.scheduled)} note="still owed or paid" />
        <Stat label="Not recorded yet" value={formatMoney(s.notYetRecorded)} note="due date has passed" />
        <Stat label="Owed back" value={formatMoney(s.owedBack)} note="releases and refunds" />
        <Stat label="Paid back" value={formatMoney(s.owedBackRecorded)} note="recorded by organizer" />
      </dl>
      <p className="text-xs text-ink-soft">The organizer holds the money. These figures come from the organizer’s records; none of them is a bank confirmation.</p>
    </Card>
  )
}

/** A member leaves a running savings group. What they paid becomes a refund the organizer owes them. */
export function LeaveGroupCard({ group, onDone }: { group: GroupDetail; onDone: () => void }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const leave = useMutation({
    mutationFn: () => api.post(`/groups/${group.id}/members/${group.myMembership!.membershipId}/exit`, { reason }),
    onSuccess: () => { setOpen(false); onDone() },
  })
  const paid = group.savings?.recorded
  return (
    <Card className="space-y-3">
      <h2 className="font-bold">Leaving early</h2>
      {!open ? (
        <>
          <p className="text-sm text-ink-soft">If you leave, you get back what was recorded as paid by you{paid ? ` (${formatMoney(paid)} so far)` : ''}, and your future contributions are dropped.</p>
          <Button variant="secondary" onClick={() => setOpen(true)}>Leave this group</Button>
        </>
      ) : (
        <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); leave.mutate() }}>
          <Alert tone="warn" title="This can’t be undone">You’ll stop contributing, and the organizer will owe you {paid ? formatMoney(paid) : 'what was recorded as paid by you'}. You’ll still see your history and the refund here.</Alert>
          <Field label="Why are you leaving?" hint="The organizer will see this.">
            {(id, d) => <TextInput id={id} aria-describedby={d} value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />}
          </Field>
          <ErrorAlert error={leave.error} />
          <div className="flex gap-2">
            <Button type="submit" variant="danger" busy={leave.isPending} disabled={reason.trim().length < 5}>Leave the group</Button>
            <Button type="button" variant="ghost" onClick={() => setOpen(false)}>Stay</Button>
          </div>
        </form>
      )}
    </Card>
  )
}

/** Organizer records that a member left early (e.g. they told the organizer in person). */
export function RecordExit({ groupId, member, onDone }: { groupId: string; member: Member; onDone: () => void }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const exit = useMutation({ mutationFn: () => api.post(`/groups/${groupId}/members/${member.membershipId}/exit`, { reason }), onSuccess: () => { setOpen(false); onDone() } })
  if (!open) return <Button variant="ghost" className="min-h-9 px-2 text-xs" onClick={() => setOpen(true)} aria-label={`Record that ${member.displayName} left`}>Record leaving</Button>
  return (
    <form className="mt-2 basis-full space-y-2 rounded-lg border border-line bg-paper p-3" onSubmit={(e) => { e.preventDefault(); exit.mutate() }}>
      <p className="text-sm">{member.displayName}’s unpaid contributions will be dropped and you’ll owe them back what was recorded as paid by them.</p>
      <Field label="Reason">
        {(id) => <TextInput id={id} value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />}
      </Field>
      <ErrorAlert error={exit.error} />
      <div className="flex gap-2">
        <Button type="submit" variant="danger" busy={exit.isPending} disabled={reason.trim().length < 5}>Record leaving</Button>
        <Button type="button" variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </form>
  )
}

/** Organizer assigns percentages (must add up to 100%) when the accepted terms say they will. */
export function SharesEditor({ groupId, members, onDone }: { groupId: string; members: Member[]; onDone: () => void }) {
  const qc = useQueryClient()
  const active = members.filter((m) => m.status === 'Active')
  const [pct, setPct] = useState<Record<string, string>>(() =>
    Object.fromEntries(active.map((m) => [m.membershipId, m.shareBasisPoints != null ? String(m.shareBasisPoints / 100) : ''])))
  const bp = (v: string) => Math.round((Number(v) || 0) * 100)
  const total = active.reduce((sum, m) => sum + bp(pct[m.membershipId] ?? ''), 0)
  const save = useMutation({
    mutationFn: () => api.put<Member[]>(`/groups/${groupId}/shares`, { shares: active.map((m) => ({ membershipId: m.membershipId, basisPoints: bp(pct[m.membershipId] ?? '') })) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['group', groupId, 'members'] }); onDone() },
  })
  const evenly = () => {
    const each = Math.floor(10_000 / active.length)
    setPct(Object.fromEntries(active.map((m, i) => [m.membershipId, String((each + (i < 10_000 - each * active.length ? 1 : 0)) / 100)])))
  }
  const saved = active.every((m) => m.shareBasisPoints != null)
  return (
    <Card className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-bold">Members’ shares</h2>
        <Button variant="ghost" onClick={evenly}>Split evenly</Button>
      </div>
      <p className="text-sm text-ink-soft">The accepted terms say you assign each member’s share of the total before the group starts. {saved ? 'Saved — you can still adjust until the start.' : 'Not saved yet.'}</p>
      <ul className="space-y-2">
        {active.map((m) => (
          <li key={m.membershipId} className="flex items-center gap-3">
            <label htmlFor={`share-${m.membershipId}`} className="flex-1 truncate">{m.displayName}</label>
            <div className="flex items-center gap-1">
              <TextInput id={`share-${m.membershipId}`} inputMode="decimal" className="w-24 text-right" value={pct[m.membershipId] ?? ''}
                onChange={(e) => setPct((p) => ({ ...p, [m.membershipId]: e.target.value }))} />
              <span aria-hidden="true">%</span>
            </div>
          </li>
        ))}
      </ul>
      <p className={`text-sm font-semibold ${total === 10_000 ? 'text-ink' : 'text-tone-danger'}`} role="status">Total {formatShare(total)} {total === 10_000 ? '✓' : '— must be exactly 100%'}</p>
      <ErrorAlert error={save.error} />
      <Button busy={save.isPending} disabled={total !== 10_000} onClick={() => save.mutate()}>Save shares</Button>
    </Card>
  )
}

/** Releases and refunds — money the organizer owes back. Members see only their own. */
export function OwedBackList({ group }: { group: GroupDetail }) {
  const q = useQuery({ queryKey: ['group', group.id, 'payouts'], queryFn: () => api.get<PayoutListItem[]>(`/groups/${group.id}/payouts`) })
  const isOrganizer = group.viewerRole === 'Organizer'
  return (
    <Card className="space-y-3">
      <Alert tone="neutral">
        The organizer holds the money and pays it back outside the app. “Scheduled” means owed, not paid. {isOrganizer ? 'Open an item to record paying it.' : 'Open yours to confirm you received it.'}
      </Alert>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data?.length === 0 && (
        <EmptyState title="Nothing is owed back yet">
          {group.status === 'Active' ? 'Money is owed back when the term is completed, when someone leaves early, or if the group is cancelled.' : 'No payments were recorded, so nothing is owed back.'}
        </EmptyState>
      )}
      {q.data && q.data.length > 0 && (
        <ul className="divide-y divide-line">
          {q.data.map((p) => (
            <li key={p.id}>
              <Link to={`/app/groups/${group.id}/payouts/${p.id}`} className="-mx-2 grid grid-cols-[1fr_auto] items-center gap-3 rounded-lg px-2 py-3 hover:bg-ink/5">
                <div className="min-w-0">
                  <p className="truncate font-semibold">{p.recipientName}{p.isYours && <span className="ml-1 text-xs font-normal text-brand-dark">(you)</span>}</p>
                  <p className="text-xs text-ink-soft">{payoutKindLabel[p.kind]} · due {formatDate(p.scheduledDate)}</p>
                </div>
                <div className="text-right">
                  <p className="font-semibold tabular-nums">{formatMoney(p.amount)}</p>
                  <PayoutStatusBadge status={p.status} />
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
