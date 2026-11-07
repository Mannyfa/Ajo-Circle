import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { DisputeStatusBadge, ObligationStatusBadge, PayoutStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, EmptyState, ErrorAlert, Loading } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatDateTime, formatMoney } from '../../lib/format'
import type { DisputeCategory, DisputeDetail, DisputeSummary, Money, ObligationDetail, PaymentInput, PaymentRecord, PayoutDetail } from '../../types/api'
import { useGroup } from '../groups/groupContext'
import { payoutKindLabel } from '../schemes/labels'
import { CorrectionForm, DisputeForm, PaymentForm, RecordHistory } from './RecordParts'

type Kind = 'contribution' | 'payout'

/**
 * Shared detail view for a contribution or a payout: what was scheduled, every record about it, and the actions the
 * server says this viewer may take. The server decides permissions; the UI only shows what it is allowed to offer.
 */
function RecordDetail({ kind, title, subtitle, status, records, disputes, canRecord, fixedAmount, maxAmount, recordPath, invalidate, notice }: {
  kind: Kind; title: string; subtitle: ReactNode; status: ReactNode; records: PaymentRecord[]; disputes: DisputeSummary[]
  canRecord: boolean; fixedAmount?: Money; maxAmount?: Money; recordPath: string; invalidate: () => void; notice: ReactNode
}) {
  const { group: g } = useGroup()
  const navigate = useNavigate()
  const [open, setOpen] = useState<{ id: string; what: 'correct' | 'dispute' } | null>(null)
  const prefix = kind === 'contribution' ? 'contribution-records' : 'payout-records'

  const record = useMutation({ mutationFn: (p: PaymentInput) => api.post(recordPath, p), onSuccess: invalidate })
  const confirm = useMutation({ mutationFn: (id: string) => api.post(`/${prefix}/${id}/confirm`), onSuccess: invalidate })
  const correct = useMutation({
    mutationFn: (v: { id: string; reason: string; replacement: PaymentInput | null }) => api.post(`/${prefix}/${v.id}/amendments`, { reason: v.reason, replacement: v.replacement }),
    onSuccess: () => { setOpen(null); invalidate() },
  })
  const dispute = useMutation({
    mutationFn: (v: { id: string; category: DisputeCategory; description: string }) => api.post<DisputeDetail>(`/${prefix}/${v.id}/dispute`, { category: v.category, description: v.description }),
    onSuccess: (d) => { invalidate(); navigate(`/app/disputes/${d.summary.id}`) },
  })

  return (
    <div className="space-y-4">
      <Link to={`/app/groups/${g.id}/${kind === 'contribution' ? 'contributions' : 'payouts'}`} className="inline-block text-sm font-semibold text-ink-soft hover:text-ink">
        ← {kind === 'contribution' ? 'All contributions' : g.fixedTermRules ? 'Money owed back' : 'Payout schedule'}
      </Link>
      <Card className="space-y-2">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <h2 className="text-lg font-bold">{title}</h2>
            <p className="text-sm text-ink-soft">{subtitle}</p>
          </div>
          {status}
        </div>
        {notice}
      </Card>

      {canRecord && (
        <Card>
          <h2 className="mb-1 font-bold">{kind === 'contribution' ? 'Record a payment' : 'Record the payout outcome'}</h2>
          <PaymentForm fixedAmount={fixedAmount} maxAmount={maxAmount} busy={record.isPending} error={record.error}
            submitLabel={kind === 'contribution' ? 'Save as recorded by organizer' : 'Save payout record'}
            intro={kind === 'contribution'
              ? 'Only record money you know was paid outside the app. The member will be asked to confirm it.'
              : 'Record what you paid the recipient outside the app. This app does not send money.'}
            onSubmit={(p) => record.mutate(p)} />
        </Card>
      )}

      <Card>
        <h2 className="mb-3 font-bold">History</h2>
        {records.length === 0
          ? <EmptyState title="Nothing recorded yet">{kind === 'contribution' ? 'No payment has been recorded for this contribution.' : 'No outcome has been recorded for this payout.'}</EmptyState>
          : (
            <RecordHistory records={records} payout={kind === 'payout'} renderActions={(r) => (
              <>
                {r.allowedActions.length > 0 && !open && (
                  <div className="mt-3 flex flex-wrap gap-2">
                    {r.allowedActions.includes('confirm') && <Button busy={confirm.isPending} onClick={() => confirm.mutate(r.id)}>{kind === 'contribution' ? 'Yes, I paid this' : 'Yes, I received this'}</Button>}
                    {r.allowedActions.includes('dispute') && <Button variant="danger" onClick={() => setOpen({ id: r.id, what: 'dispute' })}>Report a problem</Button>}
                    {r.allowedActions.includes('correct') && <Button variant="secondary" onClick={() => setOpen({ id: r.id, what: 'correct' })}>Correct or reverse</Button>}
                  </div>
                )}
                {confirm.variables === r.id && <ErrorAlert error={confirm.error} />}
                {open?.id === r.id && open.what === 'dispute' && (
                  <DisputeForm busy={dispute.isPending} error={dispute.error} onCancel={() => setOpen(null)}
                    onSubmit={(category, description) => dispute.mutate({ id: r.id, category, description })} />
                )}
                {open?.id === r.id && open.what === 'correct' && (
                  <CorrectionForm record={r} fixedAmount={fixedAmount} maxAmount={maxAmount} busy={correct.isPending} error={correct.error}
                    onCancel={() => setOpen(null)} onSubmit={(reason, replacement) => correct.mutate({ id: r.id, reason, replacement })} />
                )}
              </>
            )} />
          )}
      </Card>

      {disputes.length > 0 && (
        <Card>
          <h2 className="mb-2 font-bold">Disputes</h2>
          <ul className="divide-y divide-line">
            {disputes.map((d) => (
              <li key={d.id}>
                <Link to={`/app/disputes/${d.id}`} className="flex items-center justify-between gap-2 py-2 hover:bg-ink/5">
                  <span className="text-sm"><span className="font-mono font-semibold">{d.reference}</span> · raised {formatDateTime(d.raisedAt)}</span>
                  <DisputeStatusBadge status={d.status} />
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}

export function ContributionDetailPage() {
  const { group: g } = useGroup()
  const { obligationId = '' } = useParams()
  const qc = useQueryClient()
  const key = ['group', g.id, 'contribution', obligationId]
  const q = useQuery({ queryKey: key, queryFn: () => api.get<ObligationDetail>(`/groups/${g.id}/contributions/${obligationId}`) })
  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <ErrorAlert error={q.error} />
  const o = q.data
  const invalidate = () => { qc.invalidateQueries({ queryKey: ['group', g.id] }); qc.invalidateQueries({ queryKey: ['disputes'] }) }
  const pending = o.records.find((r) => r.allowedActions.includes('confirm'))

  return (
    <RecordDetail kind="contribution" title={`Round ${o.roundNumber} contribution`}
      subtitle={<>{o.isYou ? 'Your contribution' : o.memberName} · {formatMoney(o.amount)} due {formatDate(o.dueDate)}</>}
      status={<ObligationStatusBadge status={o.status} />} records={o.records} disputes={o.disputes}
      canRecord={o.allowedActions.includes('record')} fixedAmount={o.amount}
      recordPath={`/groups/${g.id}/contributions/${o.id}/records`} invalidate={invalidate}
      notice={pending
        ? <Alert tone="warn" title="Please check this record">The organizer says you paid {formatMoney(pending.amount)} on {formatDate(pending.paidOn)}. Confirm it if that’s right, or report a problem.</Alert>
        : <p className="text-xs text-ink-soft">Records here are what the organizer reports. They are not verified by a bank or payment provider.</p>} />
  )
}

export function PayoutDetailPage() {
  const { group: g } = useGroup()
  const { payoutId = '' } = useParams()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['group', g.id, 'payout', payoutId], queryFn: () => api.get<PayoutDetail>(`/groups/${g.id}/payouts/${payoutId}`) })
  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <ErrorAlert error={q.error} />
  const p = q.data
  const invalidate = () => { qc.invalidateQueries({ queryKey: ['group', g.id] }); qc.invalidateQueries({ queryKey: ['disputes'] }) }
  const pending = p.records.find((r) => r.allowedActions.includes('confirm'))

  return (
    <RecordDetail kind="payout" title={p.kind === 'RotatingPayout' ? `Round ${p.roundNumber} payout` : payoutKindLabel[p.kind]}
      subtitle={<>{p.isYou ? 'To you' : `To ${p.recipientName}`} · {formatMoney(p.scheduledAmount)} {p.kind === 'RotatingPayout' ? 'scheduled for' : 'owed back, due'} {formatDate(p.scheduledDate)}</>}
      status={<PayoutStatusBadge status={p.status} />} records={p.records} disputes={p.disputes}
      canRecord={p.allowedActions.includes('record')} maxAmount={p.scheduledAmount}
      recordPath={`/groups/${g.id}/payouts/${p.id}/records`} invalidate={invalidate}
      notice={pending
        ? <Alert tone="warn" title="Did you receive this?">The organizer says you were paid {formatMoney(pending.amount)} on {formatDate(pending.paidOn)}. Confirm it if that’s right, or report a problem.</Alert>
        : <p className="text-xs text-ink-soft">The payout date arriving doesn’t mean money was sent. Only a record from the organizer changes this status, and it is not bank-verified.</p>} />
  )
}
