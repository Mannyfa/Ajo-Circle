import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { Button, Card, EmptyState, ErrorAlert, Loading } from '../../components/ui'
import { api } from '../../lib/api'
import { formatDateTime } from '../../lib/format'
import type { NotificationItem, NotificationPage, Settings } from '../../types/api'
import { wsPath } from '../../lib/workspace'

const notificationsKey = ['notifications'] as const

/** Header bell. Polls gently; the count is the only thing fetched until the user opens the list. */
export function NotificationBell() {
  const q = useQuery({
    queryKey: [...notificationsKey, 'unread'],
    queryFn: () => api.get<{ unread: number }>('/me/notifications/unread-count'),
    refetchInterval: 60_000,
  })
  const n = q.data?.unread ?? 0
  return (
    <Link to={wsPath('/app/notifications')} className="relative grid size-10 place-items-center rounded-lg text-ink-soft hover:bg-ink/5 hover:text-ink"
      aria-label={n > 0 ? `Notifications, ${n} unread` : 'Notifications'}>
      <Bell aria-hidden className="size-5" />
      {n > 0 && (
        <span className="absolute -right-0.5 -top-0.5 min-w-5 rounded-full bg-tone-danger px-1 text-center text-[11px] font-bold leading-5 text-white" aria-hidden="true">
          {n > 99 ? '99+' : n}
        </span>
      )}
    </Link>
  )
}

export function NotificationsPage() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [unreadOnly, setUnreadOnly] = useState(false)
  const q = useQuery({
    queryKey: [...notificationsKey, 'list', unreadOnly],
    queryFn: () => api.get<NotificationPage>(`/me/notifications?pageSize=50${unreadOnly ? '&unreadOnly=true' : ''}`),
  })
  const refresh = () => qc.invalidateQueries({ queryKey: notificationsKey })
  const readAll = useMutation({ mutationFn: () => api.post('/me/notifications/read-all'), onSuccess: refresh })
  const open = useMutation({
    mutationFn: (n: NotificationItem) => (n.readAt ? Promise.resolve() : api.post(`/me/notifications/${n.id}/read`)),
    onSettled: (_d, _e, n) => { refresh(); navigate(n.linkPath) },
  })

  return (
    <>
      <PageHeader title="Notifications" subtitle="Updates about your groups. Records here are what the organizer reports — not bank confirmations."
        actions={(q.data?.unread ?? 0) > 0 && <Button variant="secondary" busy={readAll.isPending} onClick={() => readAll.mutate()}>Mark all as read</Button>} />
      <div className="mb-3 flex gap-1" role="group" aria-label="Filter notifications">
        {[false, true].map((u) => (
          <button key={String(u)} aria-pressed={unreadOnly === u} onClick={() => setUnreadOnly(u)}
            className={`rounded-lg px-3 py-2 text-sm font-semibold ${unreadOnly === u ? 'bg-brand text-white' : 'text-ink-soft hover:bg-ink/5'}`}>
            {u ? `Unread${q.data ? ` (${q.data.unread})` : ''}` : 'All'}
          </button>
        ))}
      </div>
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error ?? readAll.error} />
      {q.data?.items.length === 0 && <EmptyState title={unreadOnly ? 'You’re all caught up' : 'No notifications yet'}>We’ll let you know when something in your groups needs your attention.</EmptyState>}
      {q.data && q.data.items.length > 0 && (
        <Card flush>
          <ul className="divide-y divide-line">
            {q.data.items.map((n) => (
              <li key={n.id}>
                <button onClick={() => open.mutate(n)} className="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-ink/5">
                  <span className={`mt-1.5 size-2.5 shrink-0 rounded-full ${n.readAt ? 'bg-transparent' : 'bg-tone-danger'}`} aria-hidden="true" />
                  <span className="min-w-0 flex-1">
                    <span className={`block ${n.readAt ? '' : 'font-semibold'}`}>{n.title}{!n.readAt && <span className="sr-only"> (unread)</span>}</span>
                    <span className="mt-0.5 block text-sm text-ink-soft">{n.body}</span>
                    <span className="mt-1 block text-xs text-ink-soft">{formatDateTime(n.createdAt)}</span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  )
}

export function SettingsPage() {
  const qc = useQueryClient()
  const key = ['settings']
  const q = useQuery({ queryKey: key, queryFn: () => api.get<Settings>('/me/settings') })
  const save = useMutation({ mutationFn: (s: Settings) => api.put<Settings>('/me/settings', s), onSuccess: (s) => qc.setQueryData(key, s) })

  return (
    <>
      <PageHeader title="Settings" />
      {q.isLoading && <Loading />}
      <ErrorAlert error={q.error ?? save.error} />
      {q.data && (
        <Card className="space-y-3">
          <h2 className="font-bold">Email notifications</h2>
          <label className="flex min-h-11 cursor-pointer items-start gap-3">
            <input type="checkbox" className="mt-1 size-5 accent-teal" checked={q.data.emailNotifications} disabled={save.isPending}
              onChange={(e) => save.mutate({ emailNotifications: e.target.checked })} />
            <span>
              <span className="block font-semibold">Email me about my groups</span>
              <span className="block text-sm text-ink-soft">Payments recorded for you, disputes, reminders and group changes. Emails only say what happened and link back here; amounts stay in the app.</span>
            </span>
          </label>
          <p className="text-xs text-ink-soft">Security emails — verification codes and password resets — are always sent. You’ll still see every notification in the app.</p>
          {save.isSuccess && <p role="status" className="text-sm font-semibold text-teal">Saved.</p>}
        </Card>
      )}
    </>
  )
}
