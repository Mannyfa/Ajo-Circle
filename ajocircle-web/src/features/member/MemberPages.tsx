import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { ArrowRight, CalendarClock, ChevronRight, HandCoins, Layers, Link2, ReceiptText, ShieldCheck, Users, type LucideIcon } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge, ObligationStatusBadge, PayoutStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, CardHeader, EmptyState, ErrorAlert, Field, Loading, TextInput } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api } from '../../lib/api'
import { formatDate, formatMoney, frequencyNoun, lagosToday } from '../../lib/format'
import type { GroupListItem, MyObligation, MySchedule, Paged } from '../../types/api'
import { payoutKindLabel, schemeLabel } from '../schemes/labels'

const shortDate = (iso: string) => formatDate(iso).replace(/^\w+, /, '')
const recorded = new Set(['RecordedByOrganizer', 'ConfirmedByMember', 'VerifiedByProvider'])

/** Pulls the invitation token out of a pasted link (…/join/abc) or accepts the bare code. */
function inviteTokenFrom(input: string): string | null {
  const t = input.trim()
  const m = t.match(/\/join\/([A-Za-z0-9_-]{8,})/)
  if (m) return m[1]
  return /^[A-Za-z0-9_-]{8,}$/.test(t) ? t : null
}

/** "Have an invite?" — the member's way into a group. Opens the safe invitation summary before anything is joined. */
export function JoinWithInvite({ compact }: { compact?: boolean }) {
  const navigate = useNavigate()
  const [value, setValue] = useState('')
  const [error, setError] = useState<string[] | undefined>()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const token = inviteTokenFrom(value)
    if (!token) { setError(['Paste the full invite link your organizer sent, or the code at the end of it.']); return }
    navigate(`/join/${token}`)
  }
  return (
    <form onSubmit={submit} noValidate className={compact ? 'space-y-3' : 'space-y-4'}>
      <Field label="Invite link or code" hint="Your organizer shares this, usually on WhatsApp. You’ll see the group’s rules before you join." error={error}>
        {(id, d) => <TextInput id={id} aria-describedby={d} aria-invalid={!!error} value={value} placeholder="https://…/join/…" onChange={(e) => { setValue(e.target.value); setError(undefined) }} />}
      </Field>
      <Button type="submit" className="w-full sm:w-auto"><Link2 aria-hidden className="size-4" />Open invite</Button>
    </form>
  )
}

function Metric({ icon: Icon, label, value, note }: { icon: LucideIcon; label: string; value: ReactNode; note?: ReactNode }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-card p-4">
      <div className="flex items-start justify-between gap-2">
        <dt className="text-xs font-medium text-ink-soft sm:text-sm">{label}</dt>
        <Icon aria-hidden className="size-4 shrink-0 text-teal" />
      </div>
      <dd className="mt-2 text-2xl font-light tabular-nums tracking-[-0.035em] sm:text-[28px]">{value}</dd>
      {note && <dd className="mt-0.5 text-xs text-ink-soft">{note}</dd>}
    </div>
  )
}

/* ------------------------------------------------------------------ Dashboard ------------------------------------------------------------------ */

export function MemberDashboardPage() {
  const { data: me } = useMe()
  const groups = useQuery({ queryKey: ['groups'], queryFn: () => api.get<GroupListItem[]>('/groups') })
  const schedule = useQuery({ queryKey: ['me', 'schedule'], queryFn: () => api.get<MySchedule>('/me/schedule') })
  const history = useQuery({ queryKey: ['me', 'contributions', 'summary'], queryFn: () => api.get<Paged<MyObligation>>('/me/contributions?pageSize=200') })
  const firstName = me?.displayName.split(/\s+/)[0]
  const today = lagosToday()

  const active = (groups.data ?? []).filter((g) => g.status === 'Active')
  const next = schedule.data?.contributions.find((c) => c.status === 'Due' && c.dueDate >= today)
  const overdue = (schedule.data?.contributions ?? []).filter((c) => c.status === 'Due' && c.dueDate < today)
  const nextPayout = schedule.data?.payouts.find((p) => p.status === 'Scheduled')
  const recordedCount = (history.data?.items ?? []).filter((o) => recorded.has(o.status)).length

  return (
    <>
      <PageHeader title={firstName ? `Hello, ${firstName}` : 'Dashboard'} subtitle="Your groups, what’s due next and what’s been recorded for you." />
      <ErrorAlert error={groups.error ?? schedule.error} />
      {(groups.isLoading || schedule.isLoading) && <Loading />}
      {groups.data && schedule.data && (
        <div className="space-y-6">
          {overdue.length > 0 && (
            <Alert tone="warn" title={`${overdue.length} contribution${overdue.length === 1 ? '' : 's'} past the due date with nothing recorded yet`}>
              If you’ve already paid, your organizer may not have recorded it yet. <Link to="/member/schedule" className="font-semibold underline">See your schedule</Link>
            </Alert>
          )}
          <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Metric icon={Layers} label="My active groups" value={active.length} note={`${groups.data.length} in total`} />
            <Metric icon={CalendarClock} label="Next contribution" value={next ? formatMoney(next.amount) : '—'} note={next ? `${shortDate(next.dueDate)} · ${next.groupName}` : 'Nothing scheduled'} />
            <Metric icon={ReceiptText} label="Contributions recorded" value={history.isLoading ? '…' : recordedCount} note="by your organizers" />
            <Metric icon={HandCoins} label="Next payout to you" value={nextPayout ? formatMoney(nextPayout.amount) : '—'} note={nextPayout ? `${shortDate(nextPayout.scheduledDate)} · scheduled only` : 'None scheduled'} />
          </dl>

          <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr]">
            <section aria-labelledby="my-groups" className="min-w-0">
              <div className="mb-3 flex items-center justify-between gap-3">
                <h2 id="my-groups" className="text-sm font-semibold uppercase tracking-wider text-ink-soft">My groups</h2>
                <Link to="/member/groups" className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-teal hover:text-teal-dark">View all<ArrowRight aria-hidden className="size-4" /></Link>
              </div>
              {groups.data.length === 0
                ? <Card><CardHeader title="Join your first group" description="Members join with an invite from their organizer." /><JoinWithInvite compact /></Card>
                : <ul className="grid gap-3 md:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">{groups.data.slice(0, 4).map((g) => <li key={g.id}><MemberGroupCard g={g} /></li>)}</ul>}
            </section>
            <div className="space-y-6">
              <Card>
                <CardHeader title="Coming up" description="Schedule dates only. The app doesn’t move money." />
                <UpcomingList schedule={schedule.data} limit={5} />
                <Link to="/member/schedule" className="mt-2 inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-teal hover:text-teal-dark">Full schedule<ArrowRight aria-hidden className="size-4" /></Link>
              </Card>
              {groups.data.length > 0 && <Card><CardHeader title="Have another invite?" /><JoinWithInvite compact /></Card>}
              {me?.identityLevel === 'Member' && (
                <Card>
                  <ShieldCheck aria-hidden className="size-5 text-teal" />
                  <h2 className="mt-2 font-semibold">Want to run your own group?</h2>
                  <p className="mt-1 text-sm text-ink-soft">Complete an organizer check. Once approved, your account moves to the organizer workspace.</p>
                  <Link to="/member/verification" className="mt-2 inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-teal hover:text-teal-dark">Become an organizer<ArrowRight aria-hidden className="size-4" /></Link>
                </Card>
              )}
            </div>
          </div>
        </div>
      )}
    </>
  )
}

function MemberGroupCard({ g }: { g: GroupListItem }) {
  return (
    <Link to={`/member/groups/${g.id}`} className="group flex h-full flex-col rounded-[var(--radius-card)] border border-line bg-card p-4 transition-[border-color,box-shadow] hover:border-teal/40 hover:shadow-[var(--shadow-raised)]">
      <div className="flex items-start gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-lg bg-teal-soft text-teal"><Users className="size-5" /></span>
        <div className="min-w-0 flex-1">
          <p className="font-semibold break-words">{g.name}</p>
          <p className="text-xs text-ink-soft">{schemeLabel[g.schemeType]}</p>
        </div>
        <GroupStatusBadge status={g.status} />
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-3 text-xs">
        <div><dt className="text-ink-soft">You pay</dt><dd className="mt-0.5 font-semibold tabular-nums">{formatMoney(g.contribution)}<span className="font-normal text-ink-soft"> / {frequencyNoun[g.frequency]}</span></dd></div>
        <div><dt className="text-ink-soft">{g.status === 'Active' || g.status === 'Completed' ? 'Started' : 'Starts'}</dt><dd className="mt-0.5 font-semibold">{shortDate(g.startDate)}</dd></div>
      </dl>
      <span className="mt-auto inline-flex items-center gap-1 pt-3 text-sm font-semibold text-teal">Open group<ChevronRight aria-hidden className="size-4 transition-transform group-hover:translate-x-0.5" /></span>
    </Link>
  )
}

function UpcomingList({ schedule, limit }: { schedule: MySchedule; limit?: number }) {
  const today = lagosToday()
  const items = [
    ...schedule.contributions.map((c) => ({ key: `c${c.id}`, date: c.dueDate, to: `/member/groups/${c.groupId}/contributions/${c.id}`, title: `You pay ${formatMoney(c.amount)}`, sub: `${c.groupName} · Round ${c.roundNumber}`, badge: <ObligationStatusBadge status={c.status} />, late: c.dueDate < today })),
    ...schedule.payouts.map((p) => ({ key: `p${p.id}`, date: p.scheduledDate, to: `/member/groups/${p.groupId}/payouts/${p.id}`, title: `${payoutKindLabel[p.kind]}: ${formatMoney(p.amount)} to you`, sub: `${p.groupName}${p.roundNumber ? ` · Round ${p.roundNumber}` : ''}`, badge: <PayoutStatusBadge status={p.status} />, late: false })),
  ].sort((a, b) => a.date.localeCompare(b.date)).slice(0, limit ?? Infinity)
  if (items.length === 0) return <p className="text-sm text-ink-soft">Nothing scheduled. When a group starts, your dates appear here.</p>
  return (
    <ul className="divide-y divide-line">
      {items.map((i) => (
        <li key={i.key}>
          <Link to={i.to} className="-mx-2 flex min-h-14 items-center gap-3 rounded-lg px-2 py-2.5 hover:bg-paper">
            <span className={`w-12 shrink-0 text-center ${i.late ? 'text-tone-warn' : ''}`}>
              <span className="block text-[11px] font-semibold uppercase">{new Date(i.date + 'T00:00:00').toLocaleDateString('en-NG', { month: 'short' })}</span>
              <span className="block text-lg font-semibold leading-none tabular-nums">{Number(i.date.slice(8, 10))}</span>
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-semibold">{i.title}</span>
              <span className="block truncate text-xs text-ink-soft">{i.sub}{i.late ? ' · past due' : ''}</span>
            </span>
            <span className="hidden shrink-0 sm:block">{i.badge}</span>
          </Link>
        </li>
      ))}
    </ul>
  )
}

/* ------------------------------------------------------------------ Contributions ------------------------------------------------------------------ */

const PAGE = 25

export function MemberContributionsPage() {
  const q = useInfiniteQuery({
    queryKey: ['me', 'contributions', 'pages'],
    queryFn: ({ pageParam }) => api.get<Paged<MyObligation>>(`/me/contributions?page=${pageParam}&pageSize=${PAGE}`),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
  })
  const rows = q.data?.pages.flatMap((p) => p.items) ?? []
  const total = q.data?.pages[0]?.total ?? 0

  return (
    <>
      <PageHeader title="My contributions" subtitle="Every contribution you owe across your groups, newest first. Only you and your organizer see these." />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && rows.length === 0 && <EmptyState title="No contributions yet" icon={ReceiptText}>When a group you’ve joined starts, its schedule of contributions appears here.</EmptyState>}
      {rows.length > 0 && (
        <>
          <Alert tone="neutral">“Recorded by organizer” means your organizer says it was paid. It is not a bank confirmation. Open a contribution to confirm it or report a problem.</Alert>
          {/* Phone: cards. Tablet and up: a contained table. */}
          <ul className="mt-4 space-y-3 md:hidden">
            {rows.map((o) => (
              <li key={o.id}>
                <Link to={`/member/groups/${o.groupId}/contributions/${o.id}`} className="block rounded-[var(--radius-card)] border border-line bg-card p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0"><p className="truncate font-semibold">{o.groupName}</p><p className="text-xs text-ink-soft">Round {o.roundNumber} · due {shortDate(o.dueDate)}</p></div>
                    <p className="shrink-0 font-semibold tabular-nums">{formatMoney(o.amount)}</p>
                  </div>
                  <div className="mt-3 flex items-center justify-between"><ObligationStatusBadge status={o.status} /><ChevronRight aria-hidden className="size-4 text-ink-soft" /></div>
                </Link>
              </li>
            ))}
          </ul>
          <Card flush className="mt-4 hidden md:block">
            <div className="overflow-x-auto" role="region" aria-label="My contributions" tabIndex={0}>
              <table className="w-full text-sm">
                <thead className="bg-paper text-left font-mono text-[11px] uppercase tracking-[0.08em] text-ink-soft">
                  <tr><th scope="col" className="px-4 py-3">Group</th><th scope="col" className="px-4 py-3">Round</th><th scope="col" className="px-4 py-3">Due</th><th scope="col" className="px-4 py-3 text-right">Amount</th><th scope="col" className="px-4 py-3">Status</th><th scope="col" className="px-4 py-3"><span className="sr-only">Open</span></th></tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {rows.map((o) => (
                    <tr key={o.id} className="hover:bg-paper/60">
                      <td className="px-4 py-3 font-medium">{o.groupName}</td>
                      <td className="px-4 py-3 tabular-nums">{o.roundNumber}</td>
                      <td className="whitespace-nowrap px-4 py-3">{shortDate(o.dueDate)}</td>
                      <td className="px-4 py-3 text-right font-semibold tabular-nums">{formatMoney(o.amount)}</td>
                      <td className="px-4 py-3"><ObligationStatusBadge status={o.status} /></td>
                      <td className="px-4 py-3 text-right"><Link to={`/member/groups/${o.groupId}/contributions/${o.id}`} className="inline-flex min-h-9 items-center font-semibold text-teal hover:text-teal-dark">Open</Link></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
          <div className="mt-4 flex flex-wrap items-center gap-3 text-sm text-ink-soft">
            <span role="status">Showing {rows.length} of {total}</span>
            {q.hasNextPage && <Button variant="secondary" busy={q.isFetchingNextPage} onClick={() => q.fetchNextPage()}>Load more</Button>}
          </div>
        </>
      )}
    </>
  )
}

/* ------------------------------------------------------------------ Schedule ------------------------------------------------------------------ */

export function MemberSchedulePage() {
  const q = useQuery({ queryKey: ['me', 'schedule'], queryFn: () => api.get<MySchedule>('/me/schedule') })
  return (
    <>
      <PageHeader title="My schedule" subtitle="Contributions you still owe and payouts scheduled to you, across all your groups." />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && (
        <div className="grid gap-6 lg:grid-cols-[1.4fr_1fr]">
          <Card>
            <CardHeader title="Upcoming" description="In date order. A scheduled payout is a plan, not a payment." />
            <UpcomingList schedule={q.data} />
          </Card>
          <Card>
            <CardHeader title="Payouts to you" />
            {q.data.payouts.length === 0 ? <p className="text-sm text-ink-soft">No payouts or refunds scheduled to you.</p> : (
              <ul className="divide-y divide-line">
                {q.data.payouts.map((p) => (
                  <li key={p.id} className="flex items-center justify-between gap-3 py-3">
                    <div className="min-w-0"><p className="truncate text-sm font-semibold">{p.groupName}</p><p className="text-xs text-ink-soft">{payoutKindLabel[p.kind]} · {shortDate(p.scheduledDate)}</p></div>
                    <div className="text-right"><p className="text-sm font-semibold tabular-nums">{formatMoney(p.amount)}</p><PayoutStatusBadge status={p.status} /></div>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      )}
    </>
  )
}

/** Members who open the create-group address get an explanation and the way to become an organizer. */
export function MemberCannotCreatePage() {
  return (
    <>
      <PageHeader title="Only organizers create groups" back={{ to: '/member/dashboard', label: 'Dashboard' }} />
      <Card className="max-w-xl">
        <p className="text-sm text-ink-soft">Your account is a member account. To run your own groups, complete an organizer check (ID, BVN, phone and passport photo). When our team approves it, your account moves to the organizer workspace and keeps all your current groups.</p>
        <Link to="/member/verification" className="mt-4 inline-flex min-h-11 items-center gap-2 rounded-lg bg-lime px-4 text-sm font-medium text-ink hover:bg-lime-hover">Become an organizer</Link>
      </Card>
    </>
  )
}
