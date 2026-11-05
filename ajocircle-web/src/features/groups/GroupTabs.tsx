import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ObligationStatusBadge, PayoutStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, EmptyState, ErrorAlert, Loading } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatDateTime, formatMoney } from '../../lib/format'
import type { AuditEntry, CreatedInvitation, Invitation, Member, Obligation, Paged, Round, SchemePreview } from '../../types/api'
import { ScheduleTable, SchemeTerms } from '../schemes/SchemeTerms'
import { useGroup } from './groupContext'

export function GroupOverviewTab() {
  const { group: g } = useGroup()
  const mine = g.myMembership
  return (
    <div className="space-y-4">
      {mine && (
        <Card>
          <h2 className="font-bold">Your membership</h2>
          <p className="mt-1 text-sm text-ink-soft">
            You accepted terms version {mine.acceptedSchemeVersion} on {formatDateTime(mine.joinedAt)}.{' '}
            {mine.payoutPosition ? <>Your payout position is <strong className="text-ink">#{mine.payoutPosition}</strong>.</> : 'Your payout position will be set before the group starts.'}
          </p>
        </Card>
      )}
      <Card>
        <h2 className="mb-1 font-bold">{g.status === 'Draft' ? 'Draft terms' : `Accepted terms · version ${g.currentSchemeVersion}`}</h2>
        <p className="mb-4 text-sm text-ink-soft">{g.status === 'Draft' ? 'Members will see exactly this when they open an invitation.' : 'Locked. Changes after publishing require a documented amendment.'}</p>
        <SchemeTerms rules={g.rules} summary={g.summary} />
      </Card>
    </div>
  )
}

export function GroupMembersTab() {
  const { group: g } = useGroup()
  const isOrganizer = g.viewerRole === 'Organizer'
  const q = useQuery({ queryKey: ['group', g.id, 'members'], queryFn: () => api.get<Member[]>(`/groups/${g.id}/members`) })
  return (
    <div className="space-y-4">
      {isOrganizer && g.status === 'OpenForJoining' && <InvitationsPanel groupId={g.id} />}
      {isOrganizer && g.allowedActions.includes('assignPayoutOrder') && q.data && <PayoutOrderEditor key={q.data.map((m) => `${m.membershipId}:${m.payoutPosition}`).join()} groupId={g.id} members={q.data} />}
      <Card>
        <h2 className="mb-3 font-bold">Members <span className="font-normal text-ink-soft">({g.activeMemberCount} of {g.rules.memberCount})</span></h2>
        {q.isLoading && <Loading />}
        <ErrorAlert error={q.error} />
        {q.data?.length === 0 && <EmptyState title="No members yet">{isOrganizer ? 'Publish the group and share an invitation link.' : ''}</EmptyState>}
        {q.data && q.data.length > 0 && (
          <ul className="divide-y divide-line">
            {q.data.map((m) => (
              <li key={m.membershipId} className="flex items-center justify-between gap-3 py-2.5">
                <div className="min-w-0">
                  <p className="font-semibold truncate">{m.displayName}{m.isYou && <span className="ml-1 text-xs font-normal text-ink-soft">(you)</span>}</p>
                  <p className="text-xs text-ink-soft truncate">{m.email && <>{m.email} · </>}Joined {formatDateTime(m.joinedAt)} · terms v{m.acceptedSchemeVersion}</p>
                </div>
                <span className="shrink-0 rounded-lg bg-paper px-2 py-1 text-xs font-semibold tabular-nums">{m.payoutPosition ? `Payout #${m.payoutPosition}` : `Joined #${m.joinSequence}`}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}

function InvitationsPanel({ groupId }: { groupId: string }) {
  const qc = useQueryClient()
  const key = ['group', groupId, 'invitations']
  const list = useQuery({ queryKey: key, queryFn: () => api.get<Invitation[]>(`/groups/${groupId}/invitations`) })
  const [created, setCreated] = useState<CreatedInvitation | null>(null)
  const [copied, setCopied] = useState(false)
  const create = useMutation({
    mutationFn: () => api.post<CreatedInvitation>(`/groups/${groupId}/invitations`, { validityDays: 14 }),
    onSuccess: (inv) => { setCreated(inv); setCopied(false); qc.invalidateQueries({ queryKey: key }) },
  })
  const revoke = useMutation({ mutationFn: (id: string) => api.del(`/groups/${groupId}/invitations/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: key }) })
  const link = created ? `${window.location.origin}/join/${created.token}` : ''

  return (
    <Card className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-bold">Invitation links</h2>
        <Button variant="secondary" busy={create.isPending} onClick={() => create.mutate()}>+ New link</Button>
      </div>
      <ErrorAlert error={create.error ?? revoke.error} />
      {created && (
        <Alert tone="info" title="Share this link with members (shown once)">
          <div className="mt-2 flex flex-col gap-2 sm:flex-row">
            <input readOnly value={link} aria-label="Invitation link" className="min-w-0 flex-1 rounded-lg border border-line bg-card px-2 py-2 font-mono text-xs text-ink" onFocus={(e) => e.target.select()} />
            <Button variant="secondary" onClick={() => navigator.clipboard?.writeText(link).then(() => setCopied(true))}>{copied ? 'Copied ✓' : 'Copy link'}</Button>
          </div>
          <p className="mt-2 text-xs">Expires {formatDateTime(created.expiresAt)}. For security we only store a fingerprint of this link, so copy it now.</p>
        </Alert>
      )}
      {list.data && list.data.length > 0 && (
        <ul className="divide-y divide-line text-sm">
          {list.data.map((i) => (
            <li key={i.id} className="flex items-center justify-between gap-2 py-2">
              <span>
                <span className="font-semibold capitalize">{i.state.replace('_', ' ')}</span>
                <span className="text-ink-soft"> · used {i.useCount}{i.maxUses ? `/${i.maxUses}` : ''} · expires {formatDateTime(i.expiresAt)}</span>
              </span>
              {i.state === 'active' && <Button variant="ghost" busy={revoke.isPending && revoke.variables === i.id} onClick={() => revoke.mutate(i.id)}>Revoke</Button>}
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

function PayoutOrderEditor({ groupId, members }: { groupId: string; members: Member[] }) {
  const { refresh } = useGroup()
  const qc = useQueryClient()
  // Parent re-keys this component when the saved order changes, so initial state is enough.
  const [order, setOrder] = useState<Member[]>(() =>
    [...members].sort((a, b) => (a.payoutPosition ?? 1e9) - (b.payoutPosition ?? 1e9) || a.joinSequence - b.joinSequence))
  const save = useMutation({
    mutationFn: () => api.put<Member[]>(`/groups/${groupId}/payout-positions`, { membershipIds: order.map((m) => m.membershipId) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['group', groupId, 'members'] }); refresh() },
  })
  const move = (i: number, dir: -1 | 1) => setOrder((o) => {
    const j = i + dir
    if (j < 0 || j >= o.length) return o
    const c = [...o]; [c[i], c[j]] = [c[j], c[i]]; return c
  })
  const assigned = members.every((m) => m.payoutPosition)

  return (
    <Card className="space-y-3">
      <h2 className="font-bold">Payout order</h2>
      <p className="text-sm text-ink-soft">Position 1 receives the round 1 payout, and so on. {assigned ? 'Saved — you can still adjust it until the group starts.' : 'Not saved yet.'}</p>
      <ol className="space-y-1.5">
        {order.map((m, i) => (
          <li key={m.membershipId} className="flex items-center gap-2 rounded-xl border border-line px-3 py-2">
            <span className="w-8 font-bold tabular-nums">#{i + 1}</span>
            <span className="flex-1 truncate">{m.displayName}</span>
            <button className="grid size-9 place-items-center rounded-lg hover:bg-ink/5 disabled:opacity-30" disabled={i === 0} onClick={() => move(i, -1)} aria-label={`Move ${m.displayName} up`}>↑</button>
            <button className="grid size-9 place-items-center rounded-lg hover:bg-ink/5 disabled:opacity-30" disabled={i === order.length - 1} onClick={() => move(i, 1)} aria-label={`Move ${m.displayName} down`}>↓</button>
          </li>
        ))}
      </ol>
      <ErrorAlert error={save.error} />
      <Button busy={save.isPending} onClick={() => save.mutate()}>Save payout order</Button>
    </Card>
  )
}

export function GroupPayoutsTab() {
  const { group: g } = useGroup()
  const active = g.status === 'Active' || g.status === 'Completed'
  const rounds = useQuery({ queryKey: ['group', g.id, 'rounds'], queryFn: () => api.get<Round[]>(`/groups/${g.id}/rounds`), enabled: active })
  const preview = useQuery({ queryKey: ['group', g.id, 'preview'], queryFn: () => api.get<SchemePreview>(`/groups/${g.id}/schedule/preview`), enabled: !active })

  if (!active) return (
    <Card className="space-y-3">
      <Alert tone="neutral">This is the planned schedule. Rounds and payouts are created when the group starts.</Alert>
      {preview.isLoading && <Loading />}
      <ErrorAlert error={preview.error} />
      {preview.data && <ScheduleTable schedule={preview.data.schedule} />}
    </Card>
  )

  return (
    <Card className="space-y-3">
      <Alert tone="neutral">Payouts happen outside this app. “Scheduled” means planned — not funded, sent or guaranteed.</Alert>
      {rounds.isLoading && <Loading />}
      <ErrorAlert error={rounds.error} />
      {rounds.data && (
        <ul className="divide-y divide-line">
          {rounds.data.map((r) => (
            <li key={r.id} className="grid grid-cols-[auto_1fr_auto] items-center gap-3 py-3">
              <span className="grid size-9 place-items-center rounded-full bg-paper font-bold tabular-nums">{r.roundNumber}</span>
              <div className="min-w-0">
                <p className="font-semibold truncate">{r.payoutRecipientName}{g.myMembership && r.payoutRecipientName && isMine(g.myMembership.payoutPosition, r.roundNumber) && <span className="ml-1 text-xs font-normal text-brand-dark">(your turn)</span>}</p>
                <p className="text-xs text-ink-soft">Contributions due {formatDate(r.contributionDueDate)} · payout {formatDate(r.payoutDate)}</p>
              </div>
              <div className="text-right">
                <p className="font-semibold tabular-nums">{formatMoney(r.scheduledPayout)}</p>
                <PayoutStatusBadge status={r.payoutStatus} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
const isMine = (pos: number | null, round: number) => pos === round

export function GroupContributionsTab() {
  const { group: g } = useGroup()
  const [page, setPage] = useState(1)
  const active = g.status === 'Active' || g.status === 'Completed'
  const q = useQuery({
    queryKey: ['group', g.id, 'contributions', page],
    queryFn: () => api.get<Paged<Obligation>>(`/groups/${g.id}/contributions?page=${page}&pageSize=50`),
    enabled: active,
  })
  if (!active) return <EmptyState title="No contributions yet">Contribution obligations are created for every member and round when the group starts.</EmptyState>

  const pages = q.data ? Math.max(1, Math.ceil(q.data.total / q.data.pageSize)) : 1
  return (
    <Card className="space-y-3">
      <Alert tone="neutral" title="What these statuses mean">
        “Due” is an obligation, not money received. Recording payments made outside the app arrives in the next release; organizer records will be labelled “Recorded by organizer”, not verified.
      </Alert>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[480px] text-sm">
            <thead className="text-left text-xs uppercase tracking-wider text-ink-soft">
              <tr><th className="py-2 pr-2">Round</th><th className="py-2 pr-2">Member</th><th className="py-2 pr-2">Due</th><th className="py-2 pr-2 text-right">Amount</th><th className="py-2">Status</th></tr>
            </thead>
            <tbody className="divide-y divide-line">
              {q.data.items.map((o) => (
                <tr key={o.id}>
                  <td className="py-2 pr-2 tabular-nums">{o.roundNumber}</td>
                  <td className="py-2 pr-2">{o.memberName}</td>
                  <td className="py-2 pr-2 whitespace-nowrap">{formatDate(o.dueDate)}</td>
                  <td className="py-2 pr-2 text-right tabular-nums">{formatMoney(o.amount)}</td>
                  <td className="py-2"><ObligationStatusBadge status={o.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {pages > 1 && (
        <div className="flex items-center justify-between">
          <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
          <span className="text-sm text-ink-soft">Page {page} of {pages}</span>
          <Button variant="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</Button>
        </div>
      )}
    </Card>
  )
}

const actionLabels: Record<string, string> = {
  'group.created': 'Created the group',
  'group.draft_updated': 'Edited the draft',
  'group.published': 'Published terms and opened the group',
  'invitation.created': 'Created an invitation link',
  'invitation.revoked': 'Revoked an invitation link',
  'membership.joined': 'Joined and accepted the terms',
  'group.enrolment_closed': 'Enrolment closed',
  'group.payout_order_assigned': 'Payout order set',
  'group.started': 'Started the group — schedule created',
  'group.cancelled': 'Cancelled the group',
}

export function GroupActivityTab() {
  const { group: g } = useGroup()
  const q = useQuery({ queryKey: ['group', g.id, 'audit'], queryFn: () => api.get<Paged<AuditEntry>>(`/groups/${g.id}/audit-logs?pageSize=100`) })
  return (
    <Card>
      <h2 className="mb-1 font-bold">Activity</h2>
      <p className="mb-3 text-sm text-ink-soft">A permanent record of changes to this group. Entries can’t be edited or deleted.</p>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && (
        <ol className="space-y-3">
          {q.data.items.map((a) => (
            <li key={a.id} className="border-l-2 border-line pl-3">
              <p className="text-sm font-semibold">{actionLabels[a.action] ?? a.action}</p>
              <p className="text-xs text-ink-soft">{a.actorName ?? 'System'} · {formatDateTime(a.createdAt)}</p>
            </li>
          ))}
        </ol>
      )}
    </Card>
  )
}
