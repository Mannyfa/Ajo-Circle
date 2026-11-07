import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, NavLink, Outlet, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { DisputeStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, EmptyState, ErrorAlert, Field, Loading, TextInput } from '../../components/ui'
import { meKey, useMe } from '../../hooks/useMe'
import { api } from '../../lib/api'
import { formatDateTime } from '../../lib/format'
import type { AuditSearchItem, DisputeSummary, Paged, SubmissionStatus, VerificationDetail, VerificationQueueItem } from '../../types/api'
import { categoryLabel } from '../records/labels'

const idTypeLabel = { NationalId: 'National ID (NIN)', InternationalPassport: 'International passport', DriversLicence: 'Driver’s licence', VotersCard: 'Voter’s card' } as const
const docLabel = { IdFront: 'ID — front', IdBack: 'ID — back', PassportPhoto: 'Passport photograph' } as const

/**
 * Operations workspace shell (brief §5.4). The server enforces the role and the second factor on every request; this
 * only shows the right step. Staff without a recent second factor see the code prompt, nothing else.
 */
export function OperationsLayout() {
  const { data: me, isLoading } = useMe()
  if (isLoading) return <Loading />
  if (!me?.isOperations) return <EmptyState title="Not found">This page doesn’t exist.</EmptyState>
  if (!me.mfaEnabled) return <MfaSetup />
  if (!me.operationsSessionActive) return <MfaPrompt />
  const tab = ({ isActive }: { isActive: boolean }) => `whitespace-nowrap rounded-xl px-3 py-2 text-sm font-semibold ${isActive ? 'bg-ink text-white' : 'text-ink-soft hover:bg-ink/5'}`
  return (
    <>
      <PageHeader title="Operations" subtitle="Everything you open here is recorded in the audit log." />
      <nav className="-mx-4 mb-4 flex gap-1 overflow-x-auto px-4 pb-1" aria-label="Operations sections">
        <NavLink to="/operations" end className={tab}>Identity checks</NavLink>
        <NavLink to="/operations/disputes" className={tab}>Escalated disputes</NavLink>
        <NavLink to="/operations/audit" className={tab}>Audit log</NavLink>
      </nav>
      <Outlet />
    </>
  )
}

function MfaSetup() {
  const qc = useQueryClient()
  const [code, setCode] = useState('')
  const setup = useMutation({ mutationFn: () => api.post<{ secret: string; otpAuthUri: string }>('/operations/mfa/setup') })
  const enable = useMutation({ mutationFn: () => api.post('/operations/mfa/enable', { code }), onSuccess: () => qc.invalidateQueries({ queryKey: meKey }) })
  return (
    <div className="mx-auto max-w-md space-y-4">
      <PageHeader title="Set up two-step sign-in" subtitle="Operations accounts need a code from an authenticator app every 12 hours." />
      <Card className="space-y-3">
        {!setup.data ? (
          <>
            <p className="text-sm">You’ll need an authenticator app such as Google Authenticator or Microsoft Authenticator on your phone.</p>
            <ErrorAlert error={setup.error} />
            <Button busy={setup.isPending} onClick={() => setup.mutate()}>Start setup</Button>
          </>
        ) : (
          <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); enable.mutate() }}>
            <p className="text-sm">In your app, add an account and enter this key (or open the link on your phone):</p>
            <p className="break-all rounded-lg bg-paper px-3 py-2 font-mono text-sm tracking-wider">{setup.data.secret.match(/.{1,4}/g)?.join(' ')}</p>
            <a className="block text-sm underline" href={setup.data.otpAuthUri}>Open in authenticator app</a>
            <Field label="Code from the app">
              {(id) => <TextInput id={id} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
            </Field>
            <ErrorAlert error={enable.error} />
            <Button type="submit" busy={enable.isPending} disabled={code.length !== 6}>Turn on two-step sign-in</Button>
          </form>
        )}
      </Card>
    </div>
  )
}

function MfaPrompt() {
  const qc = useQueryClient()
  const [code, setCode] = useState('')
  const verify = useMutation({ mutationFn: () => api.post('/operations/mfa/verify', { code }), onSuccess: () => qc.invalidateQueries({ queryKey: meKey }) })
  return (
    <div className="mx-auto max-w-md space-y-4">
      <PageHeader title="Confirm it’s you" subtitle="Enter the 6-digit code from your authenticator app." />
      <Card>
        <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); verify.mutate() }}>
          <Field label="Authenticator code">
            {(id) => <TextInput id={id} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
          </Field>
          <ErrorAlert error={verify.error} />
          <Button type="submit" busy={verify.isPending} disabled={code.length !== 6}>Continue</Button>
        </form>
      </Card>
    </div>
  )
}

// ---------- Identity checks ----------

export function VerificationQueuePage() {
  const [status, setStatus] = useState<SubmissionStatus | 'All'>('Pending')
  const q = useQuery({ queryKey: ['ops', 'verifications', status], queryFn: () => api.get<VerificationQueueItem[]>(`/operations/verifications${status === 'All' ? '' : `?status=${status}`}`) })
  return (
    <div className="space-y-3">
      <Filters value={status} options={['Pending', 'Approved', 'Rejected', 'All']} onChange={setStatus} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data?.length === 0 && <EmptyState title="Nothing here">No identity checks with this status.</EmptyState>}
      {q.data && q.data.length > 0 && (
        <Card className="p-0 sm:p-0">
          <ul className="divide-y divide-line">
            {q.data.map((v) => (
              <li key={v.id}>
                <Link to={`/operations/verifications/${v.id}`} className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-ink/5">
                  <span className="min-w-0">
                    <span className="block truncate font-semibold">{v.userName}</span>
                    <span className="block truncate text-xs text-ink-soft">{v.userEmail} · {v.level === 'Organizer' ? 'Organizer check' : 'Member check'} · {idTypeLabel[v.idType]} · submitted {formatDateTime(v.submittedAt)}</span>
                  </span>
                  <span className="shrink-0 text-xs font-semibold">{v.status}</span>
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}

export function VerificationReviewPage() {
  const { id = '' } = useParams()
  const qc = useQueryClient()
  const key = ['ops', 'verification', id]
  const q = useQuery({ queryKey: key, queryFn: () => api.get<VerificationDetail>(`/operations/verifications/${id}`) })
  const [note, setNote] = useState('')
  const reveal = useMutation({ mutationFn: () => api.post<{ idNumber: string; bvn: string | null }>(`/operations/verifications/${id}/reveal`) })
  const decide = useMutation({
    mutationFn: (action: 'approve' | 'reject') => api.post<VerificationDetail>(`/operations/verifications/${id}/${action}`, { note: note || null }),
    onSuccess: (d) => { qc.setQueryData(key, d); qc.invalidateQueries({ queryKey: ['ops', 'verifications'] }) },
  })
  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <ErrorAlert error={q.error} />
  const v = q.data
  const s = v.summary
  return (
    <div className="space-y-4">
      <Link to="/operations" className="inline-block text-sm font-semibold text-ink-soft hover:text-ink">← Identity checks</Link>
      <Card className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <h2 className="text-lg font-bold">{s.userName}</h2>
            <p className="text-sm text-ink-soft">{s.userEmail} · {s.level === 'Organizer' ? 'Organizer check' : 'Member check'} · submitted {formatDateTime(s.submittedAt)}</p>
          </div>
          <span className="rounded-lg bg-paper px-2 py-1 text-xs font-semibold">{s.status}</span>
        </div>
        <dl className="grid gap-2 text-sm sm:grid-cols-2">
          <div><dt className="text-ink-soft">Phone (verified by code)</dt><dd className="font-mono">{v.phoneMasked}</dd></div>
          <div><dt className="text-ink-soft">{idTypeLabel[s.idType]}</dt><dd className="font-mono">{reveal.data?.idNumber ?? `•••• ${v.idNumberLast4}`}</dd></div>
          {v.bvnLast4 && <div><dt className="text-ink-soft">BVN</dt><dd className="font-mono">{reveal.data?.bvn ?? `•••• ${v.bvnLast4}`}</dd></div>}
        </dl>
        {v.sensitiveDataPurged
          ? <Alert tone="neutral">The documents and full numbers were deleted at the end of the retention period. Only the outcome remains.</Alert>
          : !reveal.data && <Button variant="ghost" busy={reveal.isPending} onClick={() => reveal.mutate()}>Show full numbers (logged)</Button>}
        <ErrorAlert error={reveal.error} />
        {s.status !== 'Pending' && <Alert tone="neutral" title={`${s.status} by ${v.reviewedByName}`}>{v.decisionNote}</Alert>}
      </Card>
      {!v.sensitiveDataPurged && (
        <div className="grid gap-4 sm:grid-cols-2">
          {v.documents.map((d) => <DocumentView key={d.id} submissionId={s.id} doc={d} />)}
        </div>
      )}
      {v.allowedActions.includes('approve') && (
        <Card className="space-y-3">
          <h2 className="font-bold">Decision</h2>
          <p className="text-sm text-ink-soft">Check that the name and photo match across the documents and the account, and that the documents look genuine. There’s no automatic BVN or NIN check yet — a licensed provider is still to be chosen.</p>
          <Field label="Note to the user" hint="Required when rejecting — tell them what to fix.">
            {(id) => <TextInput id={id} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />}
          </Field>
          <ErrorAlert error={decide.error} />
          <div className="flex flex-wrap gap-2">
            <Button busy={decide.isPending && decide.variables === 'approve'} onClick={() => decide.mutate('approve')}>Approve</Button>
            <Button variant="danger" busy={decide.isPending && decide.variables === 'reject'} disabled={note.trim().length < 5} onClick={() => decide.mutate('reject')}>Reject</Button>
          </div>
        </Card>
      )}
    </div>
  )
}

/** Fetches the decrypted file only when the reviewer asks — every view is audited on the server. */
function DocumentView({ submissionId, doc }: { submissionId: string; doc: VerificationDetail['documents'][number] }) {
  const [url, setUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const load = async () => {
    setError(null)
    const res = await fetch(`/api/v1/operations/verifications/${submissionId}/documents/${doc.id}`, { credentials: 'same-origin' })
    if (!res.ok) { setError('Could not load this document.'); return }
    setUrl(URL.createObjectURL(await res.blob()))
  }
  return (
    <Card className="space-y-2">
      <h3 className="font-semibold">{docLabel[doc.kind]}</h3>
      <p className="text-xs text-ink-soft">{doc.contentType} · {Math.round(doc.sizeBytes / 1024)} KB</p>
      {!doc.available ? <p className="text-sm text-ink-soft">Deleted.</p>
        : !url ? <Button variant="secondary" onClick={load}>View (logged)</Button>
        : doc.contentType === 'application/pdf'
          ? <a className="text-sm underline" href={url} target="_blank" rel="noreferrer">Open PDF</a>
          : <img src={url} alt={docLabel[doc.kind]} className="max-h-80 w-full rounded-lg border border-line object-contain" />}
      {error && <Alert tone="danger" title={error} />}
    </Card>
  )
}

// ---------- Disputes ----------

export function OperationsDisputesPage() {
  const [status, setStatus] = useState<'Escalated' | 'Resolved' | 'All'>('Escalated')
  const q = useQuery({ queryKey: ['ops', 'disputes', status], queryFn: () => api.get<DisputeSummary[]>(`/operations/disputes${status === 'All' ? '' : `?status=${status}`}`) })
  return (
    <div className="space-y-3">
      <Filters value={status} options={['Escalated', 'Resolved', 'All']} onChange={setStatus} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data?.length === 0 && <EmptyState title="No escalated disputes">Disputes appear here when the member or organizer escalates them.</EmptyState>}
      {q.data && q.data.length > 0 && (
        <Card className="p-0 sm:p-0">
          <ul className="divide-y divide-line">
            {q.data.map((d) => (
              <li key={d.id}>
                <Link to={`/operations/disputes/${d.id}`} className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-ink/5">
                  <span className="min-w-0">
                    <span className="block truncate font-semibold">{d.subjectLabel}</span>
                    <span className="block truncate text-xs text-ink-soft"><span className="font-mono">{d.reference}</span> · {d.groupName} · {categoryLabel[d.category]} · raised by {d.raisedByName}</span>
                  </span>
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

// ---------- Audit ----------

export function AuditSearchPage() {
  const [params, setParams] = useSearchParams()
  const [form, setForm] = useState({ action: params.get('action') ?? '', actorEmail: params.get('actorEmail') ?? '', from: params.get('from') ?? '', to: params.get('to') ?? '' })
  const page = Number(params.get('page') ?? '1')
  const qs = new URLSearchParams([...params.entries()].filter(([, v]) => v))
  const q = useQuery({ queryKey: ['ops', 'audit', qs.toString()], queryFn: () => api.get<Paged<AuditSearchItem>>(`/operations/audit-logs?${qs.toString()}&pageSize=50`) })
  const search = () => {
    const next = new URLSearchParams()
    const groupId = params.get('groupId'); if (groupId) next.set('groupId', groupId)
    Object.entries(form).forEach(([k, v]) => v && next.set(k, v))
    setParams(next)
  }
  const pages = q.data ? Math.max(1, Math.ceil(q.data.total / q.data.pageSize)) : 1
  return (
    <div className="space-y-3">
      <Card>
        <form className="grid gap-3 sm:grid-cols-4" onSubmit={(e) => { e.preventDefault(); search() }}>
          <Field label="Action starts with">{(id) => <TextInput id={id} placeholder="dispute." value={form.action} onChange={(e) => setForm({ ...form, action: e.target.value })} />}</Field>
          <Field label="Actor email">{(id) => <TextInput id={id} type="email" value={form.actorEmail} onChange={(e) => setForm({ ...form, actorEmail: e.target.value })} />}</Field>
          <Field label="From">{(id) => <TextInput id={id} type="date" value={form.from} onChange={(e) => setForm({ ...form, from: e.target.value })} />}</Field>
          <Field label="To">{(id) => <TextInput id={id} type="date" value={form.to} onChange={(e) => setForm({ ...form, to: e.target.value })} />}</Field>
          <div className="flex flex-wrap items-center gap-2 sm:col-span-4">
            <Button type="submit">Search</Button>
            {params.get('groupId') && <span className="text-sm text-ink-soft">Filtered to one group · <button type="button" className="underline" onClick={() => setParams(new URLSearchParams())}>clear</button></span>}
          </div>
        </form>
      </Card>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && (
        <Card className="overflow-x-auto p-0 sm:p-0">
          <table className="w-full min-w-[640px] text-sm">
            <thead className="bg-paper text-left text-xs uppercase tracking-wider text-ink-soft">
              <tr><th className="px-3 py-2">When</th><th className="px-3 py-2">Action</th><th className="px-3 py-2">Who</th><th className="px-3 py-2">Group</th></tr>
            </thead>
            <tbody className="divide-y divide-line">
              {q.data.items.map((a) => (
                <tr key={a.id}>
                  <td className="whitespace-nowrap px-3 py-2">{formatDateTime(a.createdAt)}</td>
                  <td className="px-3 py-2 font-mono text-xs">{a.action}</td>
                  <td className="px-3 py-2">{a.actorName ?? 'System'}{a.actorEmail && <span className="block text-xs text-ink-soft">{a.actorEmail}</span>}</td>
                  <td className="px-3 py-2">{a.groupName ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {q.data.items.length === 0 && <p className="px-3 py-6 text-center text-sm text-ink-soft">No matching entries.</p>}
        </Card>
      )}
      {pages > 1 && (
        <div className="flex items-center justify-between">
          <Button variant="secondary" disabled={page <= 1} onClick={() => { params.set('page', String(page - 1)); setParams(params) }}>Previous</Button>
          <span className="text-sm text-ink-soft">Page {page} of {pages}</span>
          <Button variant="secondary" disabled={page >= pages} onClick={() => { params.set('page', String(page + 1)); setParams(params) }}>Next</Button>
        </div>
      )}
    </div>
  )
}

function Filters<T extends string>({ value, options, onChange }: { value: T; options: T[]; onChange: (v: T) => void }) {
  return (
    <div className="flex gap-1" role="group" aria-label="Filter">
      {options.map((o) => (
        <button key={o} aria-pressed={value === o} onClick={() => onChange(o)}
          className={`rounded-xl px-3 py-2 text-sm font-semibold ${value === o ? 'bg-ink text-white' : 'text-ink-soft hover:bg-ink/5'}`}>{o}</button>
      ))}
    </div>
  )
}
