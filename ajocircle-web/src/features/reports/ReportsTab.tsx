import { useQuery } from '@tanstack/react-query'
import { Clock, Download, FileSpreadsheet, History, Landmark, ListChecks, Scale, Users, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { Alert, Button, Card, CardHeader, ErrorAlert, Loading } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatMoney } from '../../lib/format'
import { useGroup } from '../groups/groupContext'

type ReportKind = 'Roster' | 'Contributions' | 'Outstanding' | 'Payouts' | 'Timeline' | 'Disputes'
type ReportSummary = {
  forYou: boolean; groupName: string; asOf: string
  scheduledToDate: { amountMinor: number; currency: 'NGN' }; recorded: { amountMinor: number; currency: 'NGN' }
  overdueCount: number; overdueAmount: { amountMinor: number; currency: 'NGN' }
  recordedCount: number; confirmedCount: number; openDisputes: number; available: ReportKind[]
}
type ReportTable = { kind: ReportKind; title: string; groupName: string; asOf: string; columns: string[]; rows: string[][]; note: string }

/** The six reports of brief §14.2, described in plain language. */
const catalog: Record<ReportKind, { title: string; text: string; icon: LucideIcon }> = {
  Roster: { title: 'Roster', text: 'Members, when they joined, terms version and payout position or share.', icon: Users },
  Contributions: { title: 'Contributions', text: 'Every contribution by member and round, with what was recorded and confirmed.', icon: ListChecks },
  Outstanding: { title: 'Outstanding', text: 'Contributions past their due date with no payment recorded yet.', icon: Clock },
  Payouts: { title: 'Payouts', text: 'Payout order, scheduled amounts and the outcomes recorded by the organizer.', icon: Landmark },
  Timeline: { title: 'Timeline', text: 'The group’s full activity log and terms history.', icon: History },
  Disputes: { title: 'Disputes', text: 'Every dispute with its status and how it was resolved.', icon: Scale },
}

export function GroupReportsTab() {
  const { group: g } = useGroup()
  const [open, setOpen] = useState<ReportKind | null>(null)
  const q = useQuery({ queryKey: ['group', g.id, 'reports'], queryFn: () => api.get<ReportSummary>(`/groups/${g.id}/reports`) })
  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <ErrorAlert error={q.error} />
  const s = q.data
  const pct = s.scheduledToDate.amountMinor ? Math.min(100, Math.round((s.recorded.amountMinor / s.scheduledToDate.amountMinor) * 100)) : 0

  return (
    <div className="space-y-5">
      <Card>
        <CardHeader title={s.forYou ? 'Your figures' : 'Group figures'} description={`As of ${formatDate(s.asOf)}. Recorded = reported by the organizer, not verified by a bank.`} />
        <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Figure label="Due to date" value={formatMoney(s.scheduledToDate)} />
          <Figure label="Recorded" value={formatMoney(s.recorded)} note={`${s.scheduledToDate.amountMinor > 0 ? `${pct}% of what’s due` : 'Recorded ahead of due dates'} · ${s.confirmedCount} of ${s.recordedCount} confirmed`} />
          <Figure label="Overdue" value={formatMoney(s.overdueAmount)} note={`${s.overdueCount} contribution${s.overdueCount === 1 ? '' : 's'} not recorded`} tone={s.overdueCount > 0 ? 'warn' : undefined} />
          <Figure label="Open disputes" value={String(s.openDisputes)} tone={s.openDisputes > 0 ? 'danger' : undefined} />
        </dl>
        {s.scheduledToDate.amountMinor > 0 && <div className="mt-4 h-2 rounded-full bg-paper" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label="Recorded against due to date">
          <div className="h-2 rounded-full bg-brand" style={{ width: `${pct}%` }} />
        </div>}
      </Card>

      <section aria-label="Reports">
        <h2 className="mb-3 text-xs font-semibold uppercase tracking-wider text-ink-soft">Reports and exports</h2>
        <ul className="grid gap-3 md:grid-cols-2">
          {s.available.map((k) => {
            const c = catalog[k]
            return (
              <li key={k} className="flex flex-col rounded-[var(--radius-card)] border border-line bg-card p-4 shadow-[var(--shadow-card)]">
                <div className="flex gap-3">
                  <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-lg bg-brand-soft text-brand"><c.icon className="size-5" /></span>
                  <div className="min-w-0">
                    <h3 className="font-semibold">{c.title}</h3>
                    <p className="mt-0.5 text-sm text-ink-soft">{c.text}</p>
                  </div>
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  <Button variant="secondary" aria-expanded={open === k} onClick={() => setOpen(open === k ? null : k)}>{open === k ? 'Hide preview' : 'Preview'}</Button>
                  <a href={`/api/v1/groups/${g.id}/reports/${k.toLowerCase()}/csv`} download
                    className="inline-flex min-h-11 items-center gap-2 rounded-lg px-3 text-sm font-semibold text-brand hover:bg-brand-soft">
                    <Download aria-hidden className="size-4" />Download CSV
                  </a>
                </div>
              </li>
            )
          })}
        </ul>
      </section>

      {open && <ReportPreview groupId={g.id} kind={open} />}
      <p className="flex items-center gap-1.5 text-xs text-ink-soft"><FileSpreadsheet aria-hidden className="size-4" />
        {s.forYou ? 'Exports contain only what you can see in the app: your own contributions, the shared payout schedule and your disputes.' : 'Exports are recorded in the activity log. Evidence references and dispute messages are never exported.'}
      </p>
    </div>
  )
}

function Figure({ label, value, note, tone }: { label: string; value: string; note?: string; tone?: 'warn' | 'danger' }) {
  const color = tone === 'warn' ? 'text-tone-warn' : tone === 'danger' ? 'text-tone-danger' : 'text-ink'
  return (
    <div className="rounded-lg border border-line px-3 py-3">
      <dt className="text-xs font-medium uppercase tracking-wide text-ink-soft">{label}</dt>
      <dd className={`mt-1 text-lg font-semibold tabular-nums tracking-tight ${color}`}>{value}</dd>
      {note && <dd className="text-xs text-ink-soft">{note}</dd>}
    </div>
  )
}

const PREVIEW_ROWS = 50

function ReportPreview({ groupId, kind }: { groupId: string; kind: ReportKind }) {
  const q = useQuery({ queryKey: ['group', groupId, 'report', kind], queryFn: () => api.get<ReportTable>(`/groups/${groupId}/reports/${kind.toLowerCase()}`) })
  return (
    <Card flush>
      <div className="border-b border-line px-4 py-3 sm:px-5">
        <h2 className="font-semibold">{q.data?.title ?? catalog[kind].title}</h2>
        {q.data && <p className="text-xs text-ink-soft">{q.data.rows.length} row{q.data.rows.length === 1 ? '' : 's'} · {q.data.note}</p>}
      </div>
      {q.isLoading && <Loading />}
      {q.error != null && <div className="p-4"><ErrorAlert error={q.error} /></div>}
      {q.data && q.data.rows.length === 0 && <p className="px-5 py-8 text-center text-sm text-ink-soft">Nothing to report yet.</p>}
      {q.data && q.data.rows.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px] text-sm">
            <thead className="bg-paper text-left text-xs font-semibold uppercase tracking-wide text-ink-soft">
              <tr>{q.data.columns.map((c) => <th key={c} scope="col" className={`whitespace-nowrap px-4 py-2.5 ${c.includes("(NGN)") ? "text-right" : ""}`}>{c}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-line">
              {q.data.rows.slice(0, PREVIEW_ROWS).map((r, i) => (
                <tr key={i} className="hover:bg-paper/60">
                  {r.map((cell, j) => <td key={j} className={`whitespace-nowrap px-4 py-2.5 ${/^\d+\.\d{2}$/.test(cell) ? 'text-right tabular-nums' : ''}`}>{cell || <span className="text-ink-soft">—</span>}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {q.data && q.data.rows.length > PREVIEW_ROWS && (
        <div className="border-t border-line p-4"><Alert tone="neutral">Showing the first {PREVIEW_ROWS} rows. Download the CSV for all {q.data.rows.length}.</Alert></div>
      )}
    </Card>
  )
}
