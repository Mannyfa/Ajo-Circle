import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useParams } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, ErrorAlert, Field, Loading, TextInput } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatDateTime } from '../../lib/format'
import type { GroupDetail } from '../../types/api'
import { schemeLabel } from '../schemes/labels'
import { groupKey, type GroupCtx } from './groupContext'


export function GroupLayout() {
  const { groupId = '' } = useParams()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: groupKey(groupId), queryFn: () => api.get<GroupDetail>(`/groups/${groupId}`) })

  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <><PageHeader title="Group" back={{ to: '/app/groups', label: 'My groups' }} /><ErrorAlert error={q.error} /></>

  const g = q.data
  const refresh = () => qc.invalidateQueries({ queryKey: ['group', groupId] })
  const isOrganizer = g.viewerRole === 'Organizer'
  const base = `/app/groups/${g.id}`
  // Underline tabs: the corporate convention for sections of one record.
  const tab = ({ isActive }: { isActive: boolean }) =>
    `-mb-px inline-flex min-h-11 items-center whitespace-nowrap border-b-2 px-3 text-sm font-medium transition-colors ${isActive ? 'border-teal font-semibold text-ink' : 'border-transparent text-ink-soft hover:border-line-strong hover:text-ink'}`

  return (
    <>
      <PageHeader
        back={{ to: '/app/groups', label: 'My groups' }}
        title={g.name}
        subtitle={<span className="flex flex-wrap items-center gap-2"><GroupStatusBadge status={g.status} />
          <span>{isOrganizer ? 'You organize this group' : `Organized by ${g.organizerName}`}</span>
          {g.currentSchemeVersion && <span>· Terms v{g.currentSchemeVersion}</span>}
          <span>· {schemeLabel[g.schemeType]}</span></span>}
      />
      <Lifecycle status={g.status} />
      {isOrganizer && <OrganizerActions group={g} onDone={refresh} />}
      {g.status === 'Completed' && g.completedAt && (
        <div className="mb-4"><Alert tone="neutral" title="The savings term is complete">Completed on {formatDateTime(g.completedAt)}. What each member is owed is on the Money owed back tab.</Alert></div>
      )}
      {g.myMembership?.status === 'Exited' && (
        <div className="mb-4"><Alert tone="neutral" title="You left this group">You can still see your contributions and the refund owed to you.</Alert></div>
      )}
      {g.status === 'Cancelled' && (
        <div className="mb-4"><Alert tone="danger" title="This group was cancelled">{g.cancellationReason}{g.cancelledAt && <span className="block text-xs">on {formatDateTime(g.cancelledAt)}</span>}</Alert></div>
      )}
      <nav className="-mx-4 mb-5 flex gap-1 overflow-x-auto overflow-y-hidden border-b border-line px-4 sm:mx-0 sm:px-0" aria-label="Group sections">
        <NavLink to={base} end className={tab}>Overview</NavLink>
        <NavLink to={`${base}/members`} className={tab}>Members</NavLink>
        <NavLink to={`${base}/payouts`} className={tab}>{g.fixedTermRules ? 'Money owed back' : 'Payout schedule'}</NavLink>
        <NavLink to={`${base}/contributions`} className={tab}>{isOrganizer ? 'Contributions' : 'My contributions'}</NavLink>
        {g.startedAt && <NavLink to={`${base}/reports`} className={tab}>Reports</NavLink>}
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
    <ol className="mb-5 flex items-center rounded-[var(--radius-card)] border border-line bg-card px-3 py-3 shadow-[var(--shadow-card)] sm:px-5" aria-label="Group lifecycle">
      {stages.map(([s, label], i) => (
        <li key={s} aria-current={i === idx ? 'step' : undefined} className="flex flex-1 items-center gap-2 last:flex-none">
          <span className={`grid size-6 shrink-0 place-items-center rounded-full text-[11px] font-bold ${i < idx ? 'bg-teal text-white' : i === idx ? 'bg-brand text-white ring-4 ring-mint/30' : 'bg-paper text-ink-soft ring-1 ring-line-strong'}`}>
            {i < idx ? <Check aria-hidden className="size-3.5" strokeWidth={3} /> : i + 1}
          </span>
          <span className={`hidden text-xs font-semibold sm:inline ${i === idx ? 'text-ink' : 'text-ink-soft'}`}>{label}</span>
          {i < stages.length - 1 && <span aria-hidden className={`mx-1 h-px flex-1 ${i < idx ? 'bg-teal' : 'bg-line-strong'}`} />}
        </li>
      ))}
      <li className="sr-only">Current stage: {stages[idx]?.[1]}</li>
    </ol>
  )
}

function OrganizerActions({ group: g, onDone }: { group: GroupDetail; onDone: () => void }) {
  const qc = useQueryClient()
  const [cancelling, setCancelling] = useState(false)
  const [completing, setCompleting] = useState(false)
  const [reason, setReason] = useState('')
  const after = () => { onDone(); qc.invalidateQueries({ queryKey: ['groups'] }) }
  const run = useMutation({ mutationFn: (path: string) => api.post<GroupDetail>(`/groups/${g.id}/${path}`), onSuccess: after })
  const cancel = useMutation({ mutationFn: () => api.post<GroupDetail>(`/groups/${g.id}/cancel`, { reason }), onSuccess: () => { setCancelling(false); after() } })
  const can = (a: string) => g.allowedActions.includes(a)
  if (g.allowedActions.length === 0) return null

  const ft = g.fixedTermSummary
  const hint =
    g.status === 'Draft' ? 'Review the terms below, then publish to start inviting members. Published terms are locked.'
    : g.status === 'OpenForJoining' ? `${g.activeMemberCount} of ${g.capacity} members have joined. Enrolment closes automatically when the group is full${ft ? '; a savings group can also start once 2 have joined — close enrolment when you’re ready.' : '.'}`
    : g.status === 'ReadyToStart' && ft ? (can('assignShares') ? 'Set each member’s share on the Members tab, then start the group.' : `${g.activeMemberCount} members have joined. Starting the group creates everyone’s contribution schedule.`)
    : g.status === 'ReadyToStart' ? (g.activeMemberCount === g.capacity
        ? (can('assignPayoutOrder') ? 'Set the payout order on the Members tab, then start the group.' : 'Everyone has joined. Starting the group creates the contribution and payout schedule.')
        : `Only ${g.activeMemberCount} of ${g.capacity} members joined. The accepted terms need exactly ${g.capacity}, so this group can’t start as it is — cancel it and create a new one with the right size.`)
    : g.status === 'Active' && ft ? (can('complete')
        ? 'The last contribution date has arrived. Completing the term works out what each member is owed under the sharing rule.'
        : `Members contribute until ${formatDate(ft.lastDueDate)}. You can complete the term after that date. Record members leaving early on the Members tab.`)
    : ''

  return (
    <Card className="mb-4 space-y-3">
      <p className="text-sm text-ink-soft">{hint}</p>
      <div className="flex flex-wrap gap-2">
        {can('publish') && <Button busy={run.isPending && run.variables === 'publish'} onClick={() => run.mutate('publish')}>Publish & open for joining</Button>}
        {can('closeEnrolment') && <Button variant="secondary" busy={run.isPending && run.variables === 'close-enrolment'} onClick={() => run.mutate('close-enrolment')}>Close enrolment</Button>}
        {can('start') && <Button busy={run.isPending && run.variables === 'start'} onClick={() => run.mutate('start')}>Start group</Button>}
        {can('complete') && !completing && <Button onClick={() => setCompleting(true)}>Complete the term</Button>}
        {can('cancel') && !cancelling && <Button variant="danger" onClick={() => setCancelling(true)}>Cancel group</Button>}
      </div>
      <ErrorAlert error={run.error} />
      {completing && (
        <div className="space-y-3 rounded-lg border border-line bg-paper p-3">
          <p className="text-sm">This ends the term. Using the payments recorded so far and the agreed rule, the app works out what you owe back to each member. Contributions never recorded stay unpaid. You can’t undo this.</p>
          <div className="flex gap-2">
            <Button busy={run.isPending && run.variables === 'complete'} onClick={() => run.mutate('complete', { onSuccess: () => setCompleting(false) })}>Yes, complete the term</Button>
            <Button variant="ghost" onClick={() => setCompleting(false)}>Not yet</Button>
          </div>
        </div>
      )}
      {cancelling && (
        <form className="space-y-3 rounded-lg border border-tone-danger/30 bg-tone-danger-bg/50 p-3" onSubmit={(e) => { e.preventDefault(); cancel.mutate() }}>
          {g.status === 'Active' && <p className="text-sm">Cancelling a running savings group drops all unpaid contributions, and you’ll owe each member back what was recorded as paid by them.</p>}
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
