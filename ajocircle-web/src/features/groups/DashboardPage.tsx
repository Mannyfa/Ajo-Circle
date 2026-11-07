import { useQuery } from '@tanstack/react-query'
import { ChevronRight, PiggyBank, Plus, Repeat, Search, Users } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge } from '../../components/StatusBadge'
import { EmptyState, ErrorAlert, Loading, Select, TextInput } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api } from '../../lib/api'
import { formatDate, formatMoney, frequencyNoun } from '../../lib/format'
import type { GroupListItem, GroupStatus } from '../../types/api'
import { schemeLabel } from '../schemes/labels'

const primaryLink = 'inline-flex min-h-11 items-center gap-2 rounded-lg bg-brand px-4 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark'

export function DashboardPage() {
  const { data: me } = useMe()
  const q = useQuery({ queryKey: ['groups'], queryFn: () => api.get<GroupListItem[]>('/groups') })
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | GroupStatus>('all')

  const filtered = useMemo(() => (q.data ?? []).filter((g) =>
    (status === 'all' || g.status === status) && g.name.toLowerCase().includes(search.trim().toLowerCase())), [q.data, search, status])

  const all = q.data ?? []
  const organizing = filtered.filter((g) => g.viewerRole === 'Organizer')
  const memberOf = filtered.filter((g) => g.viewerRole === 'Member')
  const firstName = me?.displayName.split(/\s+/)[0]

  return (
    <>
      <PageHeader title={firstName ? `Welcome back, ${firstName}` : 'My groups'} subtitle="Your ajo groups and savings at a glance."
        actions={<Link to="/app/groups/new" className={primaryLink}><Plus aria-hidden className="size-4" />New group</Link>} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && q.data.length === 0 && (
        <EmptyState title="No groups yet" icon={Users} action={<Link to="/app/groups/new" className={primaryLink}>Create your first group</Link>}>
          Create a rotating ajo or a savings group, or open an invitation link from an organizer to join one.
        </EmptyState>
      )}
      {all.length > 0 && (
        <div className="space-y-6">
          <dl className="grid grid-cols-3 gap-3">
            <Kpi label="Active groups" value={all.filter((g) => g.status === 'Active').length} />
            <Kpi label="You organize" value={all.filter((g) => g.viewerRole === 'Organizer').length} />
            <Kpi label="You’re a member" value={all.filter((g) => g.viewerRole === 'Member').length} />
          </dl>
          <div className="grid gap-2 sm:grid-cols-[1fr_220px]">
            <div className="relative">
              <Search aria-hidden className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
              <TextInput className="pl-9" placeholder="Search by group name" aria-label="Search groups" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <Select aria-label="Filter by status" value={status} onChange={(e) => setStatus(e.target.value as typeof status)}>
              <option value="all">All statuses</option>
              <option value="Draft">Draft</option>
              <option value="OpenForJoining">Open for joining</option>
              <option value="ReadyToStart">Ready to start</option>
              <option value="Active">Active</option>
              <option value="Completed">Completed</option>
              <option value="Cancelled">Cancelled</option>
            </Select>
          </div>
          {filtered.length === 0 && <EmptyState title="No groups match your filters" icon={Search} />}
          {organizing.length > 0 && <GroupSection title="Groups you organize" groups={organizing} />}
          {memberOf.length > 0 && <GroupSection title="Groups you’re a member of" groups={memberOf} />}
        </div>
      )}
    </>
  )
}

function Kpi({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line bg-card px-4 py-3 shadow-[var(--shadow-card)]">
      <dt className="text-xs font-medium text-ink-soft">{label}</dt>
      <dd className="mt-1 text-2xl font-semibold tabular-nums tracking-tight">{value}</dd>
    </div>
  )
}

function GroupSection({ title, groups }: { title: string; groups: GroupListItem[] }) {
  return (
    <section aria-label={title}>
      <h2 className="mb-3 text-xs font-semibold uppercase tracking-wider text-ink-soft">{title} <span className="font-normal">({groups.length})</span></h2>
      <ul className="grid gap-3 md:grid-cols-2">
        {groups.map((g) => {
          const Icon = g.schemeType === 'RotatingAjo' ? Repeat : PiggyBank
          return (
            <li key={g.id}>
              <Link to={`/app/groups/${g.id}`}
                className="group flex h-full gap-4 rounded-[var(--radius-card)] border border-line bg-card p-4 shadow-[var(--shadow-card)] transition-colors hover:border-brand/40">
                <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-lg bg-brand-soft text-brand"><Icon className="size-5" /></span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <p className="font-semibold break-words text-ink">{g.name}</p>
                    <GroupStatusBadge status={g.status} />
                  </div>
                  <p className="mt-0.5 text-xs text-ink-soft">{schemeLabel[g.schemeType]}</p>
                  <dl className="mt-3 flex flex-wrap gap-x-6 gap-y-2 text-xs">
                    <div><dt className="text-ink-soft">Contribution</dt><dd className="mt-0.5 whitespace-nowrap font-semibold tabular-nums text-ink">{formatMoney(g.contribution)}<span className="font-normal text-ink-soft"> / {frequencyNoun[g.frequency]}</span></dd></div>
                    <div><dt className="text-ink-soft">Members</dt><dd className="mt-0.5 font-semibold tabular-nums text-ink">{g.memberCount} of {g.capacity}</dd></div>
                    <div><dt className="text-ink-soft">Starts</dt><dd className="mt-0.5 whitespace-nowrap font-semibold text-ink">{formatDate(g.startDate).replace(/^\w+, /, '')}</dd></div>
                  </dl>
                </div>
                <ChevronRight aria-hidden className="size-4 shrink-0 self-center text-ink-soft transition-transform group-hover:translate-x-0.5" />
              </Link>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
