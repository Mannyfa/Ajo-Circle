import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { GroupStatusBadge } from '../../components/StatusBadge'
import { EmptyState, ErrorAlert, Loading, Select, TextInput } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDate, formatMoney, frequencyNoun } from '../../lib/format'
import { schemeLabel } from '../schemes/labels'
import type { GroupListItem, GroupStatus } from '../../types/api'

export function DashboardPage() {
  const q = useQuery({ queryKey: ['groups'], queryFn: () => api.get<GroupListItem[]>('/groups') })
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | GroupStatus>('all')

  const filtered = useMemo(() => (q.data ?? []).filter((g) =>
    (status === 'all' || g.status === status) && g.name.toLowerCase().includes(search.trim().toLowerCase())), [q.data, search, status])

  const organizing = filtered.filter((g) => g.viewerRole === 'Organizer')
  const memberOf = filtered.filter((g) => g.viewerRole === 'Member')

  return (
    <>
      <PageHeader title="My groups" actions={<Link to="/app/groups/new" className="rounded-xl bg-brand px-4 py-2.5 text-sm font-semibold text-white hover:bg-brand-dark">+ New group</Link>} />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error} />
      {q.data && q.data.length === 0 && (
        <EmptyState title="No groups yet" action={<Link to="/app/groups/new" className="rounded-xl bg-brand px-4 py-2.5 text-sm font-semibold text-white">Create your first group</Link>}>
          Create a rotating ajo group, or open an invitation link from an organizer to join one.
        </EmptyState>
      )}
      {q.data && q.data.length > 0 && (
        <div className="space-y-6">
          <div className="grid gap-2 sm:grid-cols-[1fr_200px]">
            <TextInput placeholder="Search groups" aria-label="Search groups" value={search} onChange={(e) => setSearch(e.target.value)} />
            <Select aria-label="Filter by status" value={status} onChange={(e) => setStatus(e.target.value as typeof status)}>
              <option value="all">All statuses</option>
              <option value="Draft">Draft</option>
              <option value="OpenForJoining">Open for joining</option>
              <option value="ReadyToStart">Ready to start</option>
              <option value="Active">Active</option>
              <option value="Cancelled">Cancelled</option>
            </Select>
          </div>
          {filtered.length === 0 && <EmptyState title="No groups match your filters" />}
          {organizing.length > 0 && <GroupSection title="Groups you organize" groups={organizing} />}
          {memberOf.length > 0 && <GroupSection title="Groups you’re a member of" groups={memberOf} />}
        </div>
      )}
    </>
  )
}

function GroupSection({ title, groups }: { title: string; groups: GroupListItem[] }) {
  return (
    <section>
      <h2 className="mb-2 text-sm font-bold uppercase tracking-wider text-ink-soft">{title}</h2>
      <ul className="grid gap-3 sm:grid-cols-2">
        {groups.map((g) => (
          <li key={g.id}>
            <Link to={`/app/groups/${g.id}`} className="block rounded-2xl border border-line bg-card p-4 hover:border-ink/30">
              <div className="flex items-start justify-between gap-2">
                <p className="font-bold break-words">{g.name}</p>
                <GroupStatusBadge status={g.status} />
              </div>
              <p className="mt-2 text-sm text-ink-soft">
                <span className="font-semibold text-ink tabular-nums">{formatMoney(g.contribution)}</span> per {frequencyNoun[g.frequency]} · {g.memberCount}/{g.capacity} members · {schemeLabel[g.schemeType]}
              </p>
              <p className="mt-1 text-xs text-ink-soft">Starts {formatDate(g.startDate)}</p>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
