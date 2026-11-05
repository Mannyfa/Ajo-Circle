import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Alert, Button, Card, ErrorAlert, Loading } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api } from '../../lib/api'
import type { JoinResult, PublicInvitation } from '../../types/api'
import { SchemeTerms } from '../schemes/SchemeTerms'

/** Public invitation view: safe group details only, no member list (brief §4.4). */
export function JoinPage() {
  const { token = '' } = useParams()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: me } = useMe()
  const [accepted, setAccepted] = useState(false)
  const q = useQuery({ queryKey: ['invitation', token, me?.id ?? 'anon'], queryFn: () => api.get<PublicInvitation>(`/invitations/${token}`), retry: false })
  const join = useMutation({
    mutationFn: () => api.post<JoinResult>(`/invitations/${token}/join`, { acceptedSchemeVersionId: q.data!.schemeVersionId }),
    onSuccess: (r) => { qc.invalidateQueries({ queryKey: ['groups'] }); navigate(`/app/groups/${r.groupId}`) },
  })
  const here = `/join/${token}`

  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return (
    <div className="mx-auto max-w-xl px-4 py-10">
      <Alert tone="neutral" title="This invitation link isn’t valid">Check the link with your organizer — it may have been mistyped or replaced.</Alert>
    </div>
  )

  const inv = q.data
  return (
    <div className="mx-auto max-w-xl space-y-4 px-4 py-6">
      <div>
        <p className="text-sm text-ink-soft">{inv.organizerName} invited you to join</p>
        <h1 className="text-2xl font-extrabold break-words">{inv.groupName}</h1>
        <p className="mt-1 text-sm text-ink-soft">{inv.spotsRemaining} of {inv.rules.memberCount} places left · Terms version {inv.schemeVersion}</p>
      </div>

      {!inv.canJoin && <Alert tone="warn" title={inv.cannotJoinReason ?? 'This invitation cannot be used.'} />}

      <Card>
        <h2 className="mb-3 font-bold">Terms you are agreeing to</h2>
        <SchemeTerms rules={inv.rules} summary={inv.summary} />
      </Card>

      {inv.canJoin && (
        <Card className="space-y-4">
          {!me && (
            <>
              <p className="text-sm">Create an account or sign in to join. You’ll come straight back here.</p>
              <div className="flex flex-col gap-2 sm:flex-row">
                <Link to={`/register?next=${encodeURIComponent(here)}`} className="flex-1 rounded-xl bg-brand px-4 py-3 text-center font-semibold text-white">Create account</Link>
                <Link to={`/login?next=${encodeURIComponent(here)}`} className="flex-1 rounded-xl border border-line px-4 py-3 text-center font-semibold">Sign in</Link>
              </div>
            </>
          )}
          {me && !me.contactVerified && (
            <Alert tone="warn" title="Verify your email to join">
              <Link to={`/verify?next=${encodeURIComponent(here)}`} className="font-semibold underline">Enter your code</Link>
            </Alert>
          )}
          {me?.contactVerified && (
            <>
              <label className="flex items-start gap-3 text-sm">
                <input type="checkbox" className="mt-1 size-4 accent-brand" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} />
                <span>
                  I have read and accept terms version {inv.schemeVersion}. I understand that
                  payments happen outside this app, that scheduled payouts are not guaranteed, and that the organizer records payments.
                </span>
              </label>
              <ErrorAlert error={join.error} />
              <Button className="w-full" disabled={!accepted} busy={join.isPending} onClick={() => join.mutate()}>Accept terms & join group</Button>
            </>
          )}
        </Card>
      )}
    </div>
  )
}
