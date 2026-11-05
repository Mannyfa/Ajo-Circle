import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { NavLink, Outlet, useParams } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, ErrorAlert, Field, Loading, TextInput } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDateTime } from '../../lib/format'
import type { GroupDetail } from '../../types/api'
import { groupKey, type GroupCtx } from './groupContext'


export function GroupLayout() {
  const { groupId = '' } = useParams()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: groupKey(groupId), queryFn: () => api.get<GroupDetail>(`/groups/${groupId}`) })

  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <><PageHeader title="Group" back={{ to: '/app', label: 'My groups' }} /><ErrorAlert error={q.error} /></>

  const g = q.data
  const refresh = () => qc.invalidateQueries({ queryKey: ['group', groupId] })
  const isOrganizer = g.viewerRole === 'Organizer'
  const base = `/app/groups/${g.id}`
  const tab = ({ isActive }: { isActive: boolean }) =>
    `whitespace-nowrap rounded-xl px-3 py-2 text-sm font-semibold ${isActive ? 'bg-ink text-white' : 'text-ink-soft hover:bg-ink/5'}`

  return (
    <>
      <PageHeader
        back={{ to: '/app', label: 'My groups' }}
        title={g.name}
        subtitle={<span className="flex flex-wrap items-center gap-2"><GroupStatusBadge status={g.status} />
          <span>{isOrganizer ? 'You organize this group' : `Organized by ${g.organizerName}`}</span>
          {g.currentSchemeVersion && <span>· Terms v{g.currentSchemeVersion}</span>}</span>}
      />
      <Lifecycle status={g.status} />
      {isOrganizer && <OrganizerActions group={g} onDone={refresh} />}
      {g.status === 'Cancelled' && (
        <div className="mb-4"><Alert tone="danger" title="This group was cancelled">{g.cancellationReason}{g.cancelledAt && <span className="block text-xs">on {formatDateTime(g.cancelledAt)}</span>}</Alert></div>
      )}
      <nav className="-mx-4 mb-4 flex gap-1 overflow-x-auto px-4 pb-1" aria-label="Group sections">
        <NavLink to={base} end className={tab}>Overview</NavLink>
        <NavLink to={`${base}/members`} className={tab}>Members</NavLink>
        <NavLink to={`${base}/payouts`} className={tab}>Payout schedule</NavLink>
        <NavLink to={`${base}/contributions`} className={tab}>{isOrganizer ? 'Contributions' : 'My contributions'}</NavLink>
        {isOrganizer && <NavLink to={`${base}/activity`} className={tab}>Activity</NavLink>}
      </nav>
      <Outlet context={{ group: g, refresh } satisfies GroupCtx} />
    </>
  )
}

const stages = [
  ['Draft', 'Draft'], ['OpenForJoining', 'Inviting'], ['ReadyToStart', 'Ready'], ['Active', 'Active'], ['Completed', 'Completed'],
] as const

function Lifecycle({ status }: { status: GroupDetail['status'] }) {
  if (status === 'Cancelled' || status === 'Suspended') return null
  const idx = stages.findIndex(([s]) => s === status)
  return (
    <ol className="mb-4 flex items-center gap-1 text-[11px] font-semibold" aria-label="Group lifecycle">
      {stages.map(([s, label], i) => (
        <li key={s} aria-current={i === idx ? 'step' : undefined} className="flex flex-1 flex-col items-center gap-1">
          <span className={`h-1.5 w-full rounded-full ${i <= idx ? 'bg-brand' : 'bg-line'}`} />
          <span className={i === idx ? 'text-ink' : 'text-ink-soft'}>{label}</span>
        </li>
      ))}
    </ol>
  )
}

function OrganizerActions({ group: g, onDone }: { group: GroupDetail; onDone: () => void }) {
  const qc = useQueryClient()
  const [cancelling, setCancelling] = useState(false)
  const [reason, setReason] = useState('')
  const after = () => { onDone(); qc.invalidateQueries({ queryKey: ['groups'] }) }
  const run = useMutation({ mutationFn: (path: string) => api.post<GroupDetail>(`/groups/${g.id}/${path}`), onSuccess: after })
  const cancel = useMutation({ mutationFn: () => api.post<GroupDetail>(`/groups/${g.id}/cancel`, { reason }), onSuccess: () => { setCancelling(false); after() } })
  const can = (a: string) => g.allowedActions.includes(a)
  if (g.allowedActions.length === 0) return null

  const hint =
    g.status === 'Draft' ? 'Review the terms below, then publish to start inviting members. Published terms are locked.'
    : g.status === 'OpenForJoining' ? `${g.activeMemberCount} of ${g.rules.memberCount} members have joined. Enrolment closes automatically when the group is full.`
    : g.status === 'ReadyToStart' ? (g.activeMemberCount === g.rules.memberCount
        ? (can('assignPayoutOrder') ? 'Set the payout order on the Members tab, then start the group.' : 'Everyone has joined. Starting the group creates the contribution and payout schedule.')
        : `Only ${g.activeMemberCount} of ${g.rules.memberCount} members joined. The accepted terms need exactly ${g.rules.memberCount}, so this group can’t start as it is — cancel it and create a new one with the right size.`)
    : ''

  return (
    <Card className="mb-4 space-y-3">
      <p className="text-sm text-ink-soft">{hint}</p>
      <div className="flex flex-wrap gap-2">
        {can('publish') && <Button busy={run.isPending && run.variables === 'publish'} onClick={() => run.mutate('publish')}>Publish & open for joining</Button>}
        {can('closeEnrolment') && <Button variant="secondary" busy={run.isPending && run.variables === 'close-enrolment'} onClick={() => run.mutate('close-enrolment')}>Close enrolment</Button>}
        {can('start') && <Button busy={run.isPending && run.variables === 'start'} onClick={() => run.mutate('start')}>Start group</Button>}
        {can('cancel') && !cancelling && <Button variant="danger" onClick={() => setCancelling(true)}>Cancel group</Button>}
      </div>
      <ErrorAlert error={run.error} />
      {cancelling && (
        <form className="space-y-3 rounded-xl border border-tone-danger/30 bg-tone-danger-bg/50 p-3" onSubmit={(e) => { e.preventDefault(); cancel.mutate() }}>
          <Field label="Reason for cancelling" hint="Members will see this reason.">
            {(id, d) => <TextInput id={id} aria-describedby={d} value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />}
          </Field>
          <ErrorAlert error={cancel.error} />
          <div className="flex gap-2">
            <Button type="submit" variant="danger" busy={cancel.isPending} disabled={reason.trim().length < 5}>Confirm cancellation</Button>
            <Button type="button" variant="ghost" onClick={() => setCancelling(false)}>Keep group</Button>
          </div>
        </form>
      )}
    </Card>
  )
}
