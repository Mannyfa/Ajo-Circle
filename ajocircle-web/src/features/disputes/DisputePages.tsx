import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { DisputeStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, EmptyState, ErrorAlert, Loading } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDateTime } from '../../lib/format'
import type { DisputeDetail, DisputeResolution, DisputeStatus, DisputeSummary } from '../../types/api'
import { RecordHistory } from '../records/RecordParts'
import { categoryLabel } from '../records/labels'

const resolutionLabel: Record<DisputeResolution, string> = {
  WithdrawnByMember: 'Withdrawn by the member — record accepted',
  RecordCorrected: 'Organizer corrected the record',
  RecordReversed: 'Organizer reversed the record',
}

const subjectPath = (d: DisputeSummary) =>
  `/app/groups/${d.groupId}/${d.recordType === 'ContributionRecord' ? 'contributions' : 'payouts'}/${d.subjectId}`

export function DisputesPage() {
  const [status, setStatus] = useState<DisputeStatus | 'All'>('Open')
  const q = useQuery({
    queryKey: ['disputes', status],
    queryFn: () => api.get<DisputeSummary[]>(`/disputes${status === 'All' ? '' : `?status=${status}`}`),
  })
  const filter = (s: DisputeStatus | 'All', label: string) => (
    <button onClick={() => setStatus(s)} aria-pressed={status === s}
      className={`rounded-xl px-3 py-2 text-sm font-semibold ${status === s ? 'bg-ink text-white' : 'text-ink-soft hover:bg-ink/5'}`}>{label}</button>
  )
  return (
    <>
      <PageHeader title="Disputes" subtitle="Problems raised about recorded payments in your groups. Only the member who raised a dispute and the organizer can see it." />
      <div className="mb-3 flex gap-1" role="group" aria-label="Filter disputes">{filter('Open', 'Open')}{filter('Resolved', 'Resolved')}{filter('All', 'All')}</div>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data?.length === 0 && <EmptyState title={status === 'Open' ? 'No open disputes' : 'No disputes'}>If a record about you looks wrong, open it from the group and choose “Report a problem”.</EmptyState>}
      {q.data && q.data.length > 0 && (
        <Card className="p-0 sm:p-0">
          <ul className="divide-y divide-line">
            {q.data.map((d) => (
              <li key={d.id}>
                <Link to={`/app/disputes/${d.id}`} className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-ink/5">
                  <div className="min-w-0">
                    <p className="truncate font-semibold">{d.subjectLabel}</p>
                    <p className="truncate text-xs text-ink-soft"><span className="font-mono">{d.reference}</span> · {d.groupName} · {categoryLabel[d.category]} · {d.raisedByYou ? 'raised by you' : `raised by ${d.raisedByName}`}</p>
                  </div>
                  <DisputeStatusBadge status={d.status} />
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  )
}

export function DisputeDetailPage() {
  const { disputeId = '' } = useParams()
  const qc = useQueryClient()
  const key = ['disputes', 'detail', disputeId]
  const q = useQuery({ queryKey: key, queryFn: () => api.get<DisputeDetail>(`/disputes/${disputeId}`) })
  const [body, setBody] = useState('')
  const [withdrawing, setWithdrawing] = useState(false)
  const [note, setNote] = useState('')
  const refresh = (d: DisputeDetail) => {
    qc.setQueryData(key, d)
    qc.invalidateQueries({ queryKey: ['disputes'] })
    qc.invalidateQueries({ queryKey: ['group', d.summary.groupId] })
  }
  const reply = useMutation({ mutationFn: () => api.post<DisputeDetail>(`/disputes/${disputeId}/responses`, { body }), onSuccess: (d) => { setBody(''); refresh(d) } })
  const withdraw = useMutation({ mutationFn: () => api.post<DisputeDetail>(`/disputes/${disputeId}/withdraw`, { note: note || null }), onSuccess: (d) => { setWithdrawing(false); refresh(d) } })

  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <><PageHeader title="Dispute" back={{ to: '/app/disputes', label: 'Disputes' }} /><ErrorAlert error={q.error} /></>
  const d = q.data
  const s = d.summary
  const can = (a: string) => d.allowedActions.includes(a)

  return (
    <>
      <PageHeader back={{ to: '/app/disputes', label: 'Disputes' }} title={<span className="font-mono">{s.reference}</span>}
        subtitle={<span className="flex flex-wrap items-center gap-2"><DisputeStatusBadge status={s.status} /><Link className="underline" to={subjectPath(s)}>{s.subjectLabel}</Link><span>· {s.groupName}</span></span>} />
      <div className="space-y-4">
        {s.status === 'Resolved' && s.resolution && (
          <Alert tone="neutral" title={resolutionLabel[s.resolution]}>
            {d.resolvedByName} · {s.resolvedAt && formatDateTime(s.resolvedAt)}{d.resolutionNote && <> — “{d.resolutionNote}”</>}
          </Alert>
        )}
        {s.status === 'Open' && (
          <Alert tone="info">
            {can('correct')
              ? <>To resolve this, correct or reverse the record from <Link className="font-semibold underline" to={subjectPath(s)}>the {s.recordType === 'ContributionRecord' ? 'contribution' : 'payout'} page</Link>. Only {s.raisedByName} can withdraw it.</>
              : s.raisedByYou ? 'The organizer can correct or reverse the record, which resolves this. If you’re satisfied the record is right, you can withdraw the dispute.'
              : 'Waiting for the organizer to correct the record or the member to withdraw.'}
          </Alert>
        )}

        <Card className="space-y-2">
          <h2 className="font-bold">{categoryLabel[s.category]}</h2>
          <p className="whitespace-pre-wrap text-sm">{d.description}</p>
          <p className="text-xs text-ink-soft">{s.raisedByYou ? 'You' : s.raisedByName} raised this {formatDateTime(s.raisedAt)}</p>
        </Card>

        <Card>
          <h2 className="mb-3 font-bold">The disputed record</h2>
          <RecordHistory records={[d.record]} payout={s.recordType === 'PayoutRecord'} hideDisputeLink />
        </Card>

        <Card className="space-y-3">
          <h2 className="font-bold">Conversation</h2>
          {d.messages.length === 0 && <p className="text-sm text-ink-soft">No replies yet.</p>}
          <ol className="space-y-2">
            {d.messages.map((m) => (
              <li key={m.id} className={`max-w-[85%] rounded-2xl px-3 py-2 text-sm ${m.isYou ? 'ml-auto bg-ink text-white' : 'bg-paper'}`}>
                <p className="whitespace-pre-wrap">{m.body}</p>
                <p className={`mt-1 text-[11px] ${m.isYou ? 'text-white/70' : 'text-ink-soft'}`}>{m.isYou ? 'You' : m.authorName}{m.isOrganizer && ' (organizer)'} · {formatDateTime(m.createdAt)}</p>
              </li>
            ))}
          </ol>
          {can('reply') && (
            <form className="space-y-2" onSubmit={(e) => { e.preventDefault(); reply.mutate() }}>
              <label htmlFor="reply" className="sr-only">Your reply</label>
              <textarea id="reply" rows={2} maxLength={1000} value={body} onChange={(e) => setBody(e.target.value)} placeholder="Write a reply…"
                className="block w-full rounded-xl border border-line bg-card px-3 py-2 text-base focus:border-brand focus:outline-none" />
              <ErrorAlert error={reply.error} />
              <Button type="submit" variant="secondary" busy={reply.isPending} disabled={!body.trim()}>Send reply</Button>
            </form>
          )}
        </Card>

        {can('withdraw') && (
          <Card className="space-y-3">
            {!withdrawing ? (
              <Button variant="secondary" onClick={() => setWithdrawing(true)}>Withdraw dispute — the record is right</Button>
            ) : (
              <>
                <p className="text-sm">Withdrawing marks the record as confirmed by you. If something is still wrong later, you can raise a new dispute.</p>
                <label htmlFor="wnote" className="block text-sm font-semibold">Note (optional)</label>
                <input id="wnote" value={note} maxLength={500} onChange={(e) => setNote(e.target.value)}
                  className="block w-full min-h-11 rounded-xl border border-line bg-card px-3 py-2 text-base focus:border-brand focus:outline-none" />
                <ErrorAlert error={withdraw.error} />
                <div className="flex gap-2">
                  <Button busy={withdraw.isPending} onClick={() => withdraw.mutate()}>Confirm withdrawal</Button>
                  <Button variant="ghost" onClick={() => setWithdrawing(false)}>Keep it open</Button>
                </div>
              </>
            )}
          </Card>
        )}
      </div>
    </>
  )
}
