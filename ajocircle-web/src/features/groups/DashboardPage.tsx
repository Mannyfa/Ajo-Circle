import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, ArrowRight, CalendarClock, ChevronRight, FilePen, Layers, PiggyBank, Plus, Repeat, Scale, Search, UserPlus, Users, X, type LucideIcon } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge } from '../../components/StatusBadge'
import { Card, CardHeader, EmptyState, ErrorAlert, Loading, Select, TextInput } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api } from '../../lib/api'
import { formatDate, formatMoney, frequencyNoun } from '../../lib/format'
import type { DisputeSummary, GroupListItem, GroupStatus, SchemeType } from '../../types/api'
import { schemeLabel } from '../schemes/labels'
import { wsPath } from '../../lib/workspace'

const primaryLink = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-brand px-4 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark'
const shortDate = (iso: string) => formatDate(iso).replace(/^\w+, /, '')

function useGroups() {
  return useQuery({ queryKey: ['groups'], queryFn: () => api.get<GroupListItem[]>('/groups') })
}

/* ---------------------------------------------------------------- Dashboard ---------------------------------------------------------------- */

export function DashboardPage() {
  const { data: me } = useMe()
  const q = useGroups()
  const disputes = useQuery({ queryKey: ['disputes', 'Open'], queryFn: () => api.get<DisputeSummary[]>('/disputes?status=Open') })
  const firstName = me?.displayName.split(/\s+/)[0]
  const all = q.data ?? []

  const live = all.filter((g) => g.status === 'Active')
  const organizing = all.filter((g) => g.viewerRole === 'Organizer')
  const membersInLive = live.filter((g) => g.viewerRole === 'Organizer').reduce((n, g) => n + g.memberCount, 0)
  const drafts = organizing.filter((g) => g.status === 'Draft')
  const inviting = organizing.filter((g) => g.status === 'OpenForJoining' || g.status === 'ReadyToStart')
  const openDisputes = disputes.data ?? []
  const [today] = useState(() => new Date(new Date().toDateString()))
  const upcoming = all.filter((g) => (g.status === 'OpenForJoining' || g.status === 'ReadyToStart') && new Date(g.startDate) >= today)
    .sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5)
  const running = all.filter((g) => g.status !== 'Cancelled' && g.status !== 'Completed').slice(0, 4)

  return (
    <>
      <PageHeader title={firstName ? `Welcome back, ${firstName}` : 'Dashboard'} subtitle="Your groups and what needs your attention today."
        actions={<Link to={wsPath('/app/groups/new')} className={primaryLink}><Plus aria-hidden className="size-4" />Create group</Link>} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && all.length === 0 && (
        <EmptyState title="No groups yet" icon={Users} action={<Link to={wsPath('/app/groups/new')} className={primaryLink}>Create your first group</Link>}>
          Create a rotating ajo or a savings group, or open an invitation link from an organizer to join one.
        </EmptyState>
      )}
      {all.length > 0 && (
        <div className="space-y-6">
          <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <MetricCard icon={Layers} label="Active groups" value={live.length} note={`${all.length} in total`} />
            <MetricCard icon={Users} label="Members in groups you run" value={membersInLive} note="across active groups" />
            <MetricCard icon={UserPlus} label="Still inviting" value={inviting.length} note="groups not yet started" />
            <MetricCard icon={Scale} label="Open disputes" value={disputes.isLoading ? '…' : openDisputes.length} note="raised by or about you" tone={openDisputes.length > 0 ? 'warn' : undefined} />
          </dl>

          <div className="grid gap-6 lg:grid-cols-[1.6fr_1fr]">
            <section aria-labelledby="your-groups" className="min-w-0">
              <div className="mb-3 flex items-center justify-between gap-3">
                <h2 id="your-groups" className="text-sm font-semibold uppercase tracking-wider text-ink-soft">Your groups</h2>
                <Link to={wsPath('/app/groups')} className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-teal hover:text-teal-dark">View all<ArrowRight aria-hidden className="size-4" /></Link>
              </div>
              {running.length === 0
                ? <EmptyState title="No running groups" icon={Layers}>Completed and cancelled groups are on the My Groups page.</EmptyState>
                : <ul className="grid gap-3 md:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">{running.map((g) => <li key={g.id}><GroupCard g={g} /></li>)}</ul>}
            </section>

            <div className="space-y-6">
              <Card>
                <CardHeader title="Needs attention" />
                {disputes.error != null && <ErrorAlert error={disputes.error} />}
                <AttentionList items={[
                  ...openDisputes.slice(0, 3).map((d) => ({ key: d.id, icon: AlertTriangle, to: wsPath(`/app/disputes/${d.id}`), title: d.subjectLabel, text: `Dispute open · ${d.groupName}` })),
                  ...drafts.map((g) => ({ key: g.id, icon: FilePen, to: wsPath(`/app/groups/${g.id}`), title: g.name, text: 'Setup not finished — publish to start inviting' })),
                  ...inviting.filter((g) => g.memberCount < g.capacity).map((g) => ({ key: g.id, icon: UserPlus, to: wsPath(`/app/groups/${g.id}/members`), title: g.name, text: `${g.capacity - g.memberCount} place${g.capacity - g.memberCount === 1 ? '' : 's'} left to fill` })),
                ]} />
              </Card>
              <Card>
                <CardHeader title="Starting soon" description="Start dates from each group’s schedule." />
                {upcoming.length === 0 ? <p className="text-sm text-ink-soft">No groups waiting to start.</p> : (
                  <ul className="divide-y divide-line">
                    {upcoming.map((g) => (
                      <li key={g.id}>
                        <Link to={wsPath(`/app/groups/${g.id}`)} className="-mx-2 flex min-h-12 items-center gap-3 rounded-lg px-2 py-2 hover:bg-paper">
                          <CalendarClock aria-hidden className="size-5 shrink-0 text-teal" />
                          <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{g.name}</span><span className="block text-xs text-ink-soft">Starts {shortDate(g.startDate)} · {g.memberCount} of {g.capacity} joined</span></span>
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

function AttentionList({ items }: { items: { key: string; icon: LucideIcon; to: string; title: string; text: string }[] }) {
  if (items.length === 0) return <p className="text-sm text-ink-soft">Nothing needs your attention right now.</p>
  return (
    <ul className="divide-y divide-line">
      {items.slice(0, 6).map((i) => (
        <li key={i.key}>
          <Link to={i.to} className="-mx-2 flex min-h-12 items-center gap-3 rounded-lg px-2 py-2 hover:bg-paper">
            <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-lg bg-tone-warn-bg text-tone-warn"><i.icon className="size-4" /></span>
            <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{i.title}</span><span className="block text-xs text-ink-soft">{i.text}</span></span>
            <ChevronRight aria-hidden className="size-4 shrink-0 text-ink-soft" />
          </Link>
        </li>
      ))}
    </ul>
  )
}

function MetricCard({ icon: Icon, label, value, note, tone }: { icon: LucideIcon; label: string; value: ReactNode; note?: string; tone?: 'warn' }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-card p-4 shadow-[var(--shadow-card)]">
      <div className="flex items-start justify-between gap-2">
        <dt className="text-xs font-medium text-ink-soft sm:text-sm">{label}</dt>
        <Icon aria-hidden className={`size-4 shrink-0 ${tone === 'warn' ? 'text-amber' : 'text-teal'}`} />
      </div>
      <dd className={`mt-2 text-2xl font-semibold tabular-nums tracking-tight ${tone === 'warn' ? 'text-tone-warn' : ''}`}>{value}</dd>
      {note && <dd className="mt-0.5 text-xs text-ink-soft">{note}</dd>}
    </div>
  )
}

/* ---------------------------------------------------------------- Groups list ---------------------------------------------------------------- */

type Sort = 'name' | 'start' | 'status'
const statusOrder: GroupStatus[] = ['Active', 'ReadyToStart', 'OpenForJoining', 'Draft', 'Suspended', 'Completed', 'Cancelled']

/** Filters live in the URL so they survive refresh and back navigation. */
export function GroupsPage() {
  const q = useGroups()
  const { data: me } = useMe()
  const canCreate = me?.role === 'Organizer'
  const [params, setParams] = useSearchParams()
  const search = params.get('q') ?? ''
  const status = (params.get('status') ?? 'all') as 'all' | GroupStatus
  const scheme = (params.get('scheme') ?? 'all') as 'all' | 'rotating' | 'fixed'
  const sort = (params.get('sort') ?? 'status') as Sort
  const set = (k: string, v: string, fallback: string) => {
    const next = new URLSearchParams(params)
    if (v === fallback || v === '') next.delete(k); else next.set(k, v)
    setParams(next, { replace: true })
  }
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    const matchScheme = (s: SchemeType) => scheme === 'all' || (scheme === 'rotating' ? s === 'RotatingAjo' : s !== 'RotatingAjo')
    const rows = (q.data ?? []).filter((g) => (status === 'all' || g.status === status) && matchScheme(g.schemeType) && g.name.toLowerCase().includes(term))
    return rows.sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name) : sort === 'start' ? a.startDate.localeCompare(b.startDate) : statusOrder.indexOf(a.status) - statusOrder.indexOf(b.status))
  }, [q.data, search, status, scheme, sort])
  const filtersOn = search !== '' || status !== 'all' || scheme !== 'all'
  const organizing = filtered.filter((g) => g.viewerRole === 'Organizer')
  const memberOf = filtered.filter((g) => g.viewerRole === 'Member')

  return (
    <>
      <PageHeader title="My Groups" subtitle={canCreate ? 'Groups you organize and groups you belong to.' : 'The groups you belong to.'}
        actions={canCreate ? <Link to={wsPath('/app/groups/new')} className={primaryLink}><Plus aria-hidden className="size-4" />Create group</Link> : undefined} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && q.data.length === 0 && (
        canCreate ? (
          <EmptyState title="No groups yet" icon={Users} action={<Link to={wsPath('/app/groups/new')} className={primaryLink}>Create your first group</Link>}>
            Create a group, or open an invitation link from an organizer to join one.
          </EmptyState>
        ) : (
          <EmptyState title="You haven’t joined a group yet" icon={Users} action={<Link to="/member/dashboard" className={primaryLink}>Enter an invite link</Link>}>
            Ask your organizer for their group’s invite link.
          </EmptyState>
        )
      )}
      {q.data && q.data.length > 0 && (
        <div className="space-y-6">
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-[1fr_180px_180px_170px]">
            <div className="relative sm:col-span-2 lg:col-span-1">
              <Search aria-hidden className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
              <TextInput type="search" className="pl-9" placeholder="Search by group name" aria-label="Search groups" value={search} onChange={(e) => set('q', e.target.value, '')} />
            </div>
            <Select aria-label="Filter by savings type" value={scheme} onChange={(e) => set('scheme', e.target.value, 'all')}>
              <option value="all">All types</option>
              <option value="rotating">Rotating ajo</option>
              <option value="fixed">Fixed-term savings</option>
            </Select>
            <Select aria-label="Filter by status" value={status} onChange={(e) => set('status', e.target.value, 'all')}>
              <option value="all">All statuses</option>
              <option value="Draft">Draft</option>
              <option value="OpenForJoining">Open for joining</option>
              <option value="ReadyToStart">Ready to start</option>
              <option value="Active">Active</option>
              <option value="Completed">Completed</option>
              <option value="Cancelled">Cancelled</option>
            </Select>
            <Select aria-label="Sort groups" value={sort} onChange={(e) => set('sort', e.target.value, 'status')}>
              <option value="status">Sort: status</option>
              <option value="name">Sort: name</option>
              <option value="start">Sort: start date</option>
            </Select>
          </div>
          <div className="flex flex-wrap items-center gap-3 text-sm text-ink-soft" role="status">
            <span>{filtered.length} of {q.data.length} group{q.data.length === 1 ? '' : 's'}</span>
            {filtersOn && <button type="button" onClick={() => setParams(sort === 'status' ? {} : { sort }, { replace: true })} className="inline-flex min-h-9 items-center gap-1 rounded-lg px-2 font-semibold text-teal hover:bg-teal-soft"><X aria-hidden className="size-4" />Clear filters</button>}
          </div>
          {filtered.length === 0 && <EmptyState title="No groups match your filters" icon={Search} />}
          {organizing.length > 0 && <GroupSection title="Groups you organize" groups={organizing} />}
          {memberOf.length > 0 && <GroupSection title={canCreate ? 'Groups you’re a member of' : 'Your groups'} groups={memberOf} />}
        </div>
      )}
    </>
  )
}

function GroupSection({ title, groups }: { title: string; groups: GroupListItem[] }) {
  return (
    <section aria-label={title}>
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wider text-ink-soft">{title} <span className="font-normal">({groups.length})</span></h2>
      <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">{groups.map((g) => <li key={g.id}><GroupCard g={g} /></li>)}</ul>
    </section>
  )
}

function GroupCard({ g }: { g: GroupListItem }) {
  const Icon = g.schemeType === 'RotatingAjo' ? Repeat : PiggyBank
  const fill = Math.min(100, Math.round((g.memberCount / Math.max(1, g.capacity)) * 100))
  return (
    <Link to={wsPath(`/app/groups/${g.id}`)}
      className="group flex h-full flex-col rounded-[var(--radius-card)] border border-line bg-card p-4 shadow-[var(--shadow-card)] transition-[border-color,box-shadow] hover:border-teal/40 hover:shadow-[var(--shadow-raised)]">
      <div className="flex items-start gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-lg bg-teal-soft text-teal"><Icon className="size-5" /></span>
        <div className="min-w-0 flex-1">
          <p className="font-semibold break-words text-ink">{g.name}</p>
          <p className="text-xs text-ink-soft">{schemeLabel[g.schemeType]} · {g.viewerRole === 'Organizer' ? 'You organize' : 'Member'}</p>
        </div>
        <GroupStatusBadge status={g.status} />
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 text-xs">
        <div><dt className="text-ink-soft">Contribution</dt><dd className="mt-0.5 font-semibold tabular-nums text-ink">{formatMoney(g.contribution)}<span className="font-normal text-ink-soft"> / {frequencyNoun[g.frequency]}</span></dd></div>
        <div><dt className="text-ink-soft">{g.status === 'Active' || g.status === 'Completed' ? 'Started' : 'Starts'}</dt><dd className="mt-0.5 font-semibold text-ink">{shortDate(g.startDate)}</dd></div>
      </dl>
      <div className="mt-auto pt-4">
        <div className="flex justify-between text-xs text-ink-soft"><span>Members</span><span className="font-medium tabular-nums text-ink">{g.memberCount} of {g.capacity}</span></div>
        <div className="mt-1.5 h-1.5 rounded-full bg-paper" role="progressbar" aria-label="Places filled" aria-valuemin={0} aria-valuemax={100} aria-valuenow={fill}>
          <div className="h-1.5 rounded-full bg-teal" style={{ width: `${fill}%` }} />
        </div>
      </div>
      <span className="mt-3 inline-flex items-center gap-1 text-sm font-semibold text-teal">View group<ChevronRight aria-hidden className="size-4 transition-transform group-hover:translate-x-0.5" /></span>
    </Link>
  )
}
