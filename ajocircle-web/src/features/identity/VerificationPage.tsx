import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { Alert, Button, Card, ErrorAlert, Field, Loading, Select, TextInput } from '../../components/ui'
import { meKey } from '../../hooks/useMe'
import { api, ApiError } from '../../lib/api'
import { formatDateTime } from '../../lib/format'
import type { AccountRole, IdDocumentType, IdentityLevel, VerificationStatus } from '../../types/api'
import { useMe } from '../../hooks/useMe'

const idTypeLabel: Record<IdDocumentType, string> = {
  NationalId: 'National ID (NIN)', InternationalPassport: 'International passport', DriversLicence: 'Driver’s licence', VotersCard: 'Voter’s card',
}
const levelLabel: Record<IdentityLevel, string> = { None: 'Not verified', Member: 'Verified member', Organizer: 'Verified organizer' }
const key = ['verification']
const fe = (e: unknown, k: string) => (e instanceof ApiError ? e.fieldErrors[k] : undefined)

/** Owner decision 7: members verify phone + one ID; organizers also give BVN and a passport photo. */
export function VerificationPage() {
  const { data: me } = useMe()
  const role = me?.role ?? 'Member'
  const q = useQuery({ queryKey: key, queryFn: () => api.get<VerificationStatus>('/me/verification') })
  if (q.isLoading) return <Loading />
  if (q.error || !q.data) return <ErrorAlert error={q.error} />
  const s = q.data
  const pending = s.latest?.status === 'Pending'

  return (
    <>
      <PageHeader title={role === 'Member' && s.level === 'Member' ? 'Become an organizer' : 'Verify your identity'} subtitle={`Status: ${levelLabel[s.level]}`} />
      <div className="space-y-4">
        <Alert tone="neutral" title="Why we ask">
          Everyone in a group trusts each other with money, so members verify their phone and an ID before joining, and organizers also give
          their BVN and a passport photo before running a group. Our operations team checks each submission. Your documents and numbers are
          encrypted, only the team can view them (every view is logged), and the documents are deleted after the check is decided.
        </Alert>
        {s.level === 'Organizer' && <Alert tone="info" title="You’re verified as an organizer">You can create, run and join groups from the organizer workspace.</Alert>}
        {role === 'Member' && s.level === 'Member' && !pending && <Alert tone="info" title="Want to run your own groups?">Submit an organizer check below. When our team approves it, your account moves to the organizer workspace. Your groups and records stay as they are.</Alert>}
        {s.latest?.status === 'Rejected' && <Alert tone="danger" title="Your last check wasn’t approved">{s.latest.rejectionReason} You can submit again below.</Alert>}
        {pending && <Alert tone="info" title="We’re checking your details">Submitted {formatDateTime(s.latest!.submittedAt)}. You’ll get a notification when it’s decided.</Alert>}
        {s.level !== 'Organizer' && !pending && (
          <>
            <PhoneStep s={s} />
            {s.phoneVerified && <SubmitStep current={s.level} role={role} />}
          </>
        )}
      </div>
    </>
  )
}

function PhoneStep({ s }: { s: VerificationStatus }) {
  const qc = useQueryClient()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [sent, setSent] = useState(false)
  const request = useMutation({ mutationFn: () => api.post('/me/phone', { phone }), onSuccess: () => setSent(true) })
  const verify = useMutation({
    mutationFn: () => api.post<VerificationStatus>('/me/phone/verify', { code }),
    onSuccess: (v) => { qc.setQueryData(key, v); qc.invalidateQueries({ queryKey: meKey }); setSent(false) },
  })
  return (
    <Card className="space-y-3">
      <h2 className="font-bold">1. Phone number {s.phoneVerified && <span className="text-sm font-normal text-ink-soft">— verified ({s.phoneMasked})</span>}</h2>
      {(!s.phoneVerified || sent) && (!sent ? (
        <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); request.mutate() }}>
          <Field label="Nigerian mobile number" hint="We’ll text you a 6-digit code." error={fe(request.error, 'phone')}>
            {(id, d) => <TextInput id={id} aria-describedby={d} type="tel" inputMode="tel" autoComplete="tel" placeholder="0803 123 4567" value={phone} onChange={(e) => setPhone(e.target.value)} />}
          </Field>
          {request.error && !(request.error instanceof ApiError && request.error.status === 400) && <ErrorAlert error={request.error} />}
          <Button type="submit" busy={request.isPending} disabled={phone.replace(/\D/g, '').length < 10}>Send code</Button>
        </form>
      ) : (
        <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); verify.mutate() }}>
          <Field label="Code from the text message">
            {(id) => <TextInput id={id} inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />}
          </Field>
          <ErrorAlert error={verify.error} />
          <div className="flex gap-2">
            <Button type="submit" busy={verify.isPending} disabled={code.length !== 6}>Verify phone</Button>
            <Button type="button" variant="ghost" onClick={() => setSent(false)}>Use another number</Button>
          </div>
          {import.meta.env.DEV && <p className="text-xs text-ink-soft">Developer: texts go to the test inbox at <a className="underline" href="http://localhost:8025" target="_blank" rel="noreferrer">localhost:8025</a>.</p>}
        </form>
      ))}
    </Card>
  )
}

function SubmitStep({ current, role }: { current: IdentityLevel; role: AccountRole }) {
  const qc = useQueryClient()
  const [level, setLevel] = useState<'Member' | 'Organizer'>(current === 'Member' || role === 'Organizer' ? 'Organizer' : 'Member')
  const [idType, setIdType] = useState<IdDocumentType>('NationalId')
  const [idNumber, setIdNumber] = useState('')
  const [bvn, setBvn] = useState('')
  const [files, setFiles] = useState<{ idFront?: File; idBack?: File; passportPhoto?: File }>({})
  const [consent, setConsent] = useState(false)
  const submit = useMutation({
    mutationFn: () => {
      const f = new FormData()
      f.append('level', level); f.append('idType', idType); f.append('idNumber', idNumber)
      if (level === 'Organizer') f.append('bvn', bvn)
      if (files.idFront) f.append('idFront', files.idFront)
      if (files.idBack) f.append('idBack', files.idBack)
      if (level === 'Organizer' && files.passportPhoto) f.append('passportPhoto', files.passportPhoto)
      return api.postForm<VerificationStatus>('/me/verification', f)
    },
    onSuccess: (v) => qc.setQueryData(key, v),
  })
  const pick = (k: keyof typeof files) => (e: React.ChangeEvent<HTMLInputElement>) => setFiles((x) => ({ ...x, [k]: e.target.files?.[0] }))
  const fileCls = 'block w-full text-sm file:mr-3 file:rounded-lg file:border-0 file:bg-ink/10 file:px-3 file:py-2 file:font-semibold'

  return (
    <Card>
      <form className="space-y-4" onSubmit={(e: FormEvent) => { e.preventDefault(); submit.mutate() }} noValidate>
        <h2 className="font-bold">2. Your ID</h2>
        <fieldset className="grid gap-2 sm:grid-cols-2">
          <legend className="mb-2 text-sm font-semibold">What do you want to do?</legend>
          {current === 'None' && (
            <label className={`flex cursor-pointer gap-3 rounded-lg p-3 ${level === 'Member' ? 'border-2 border-teal bg-teal-soft' : 'border border-line'}`}>
              <input type="radio" name="level" checked={level === 'Member'} onChange={() => setLevel('Member')} className="mt-1 accent-teal" />
              <span><span className="block font-semibold">Join groups</span><span className="text-sm text-ink-soft">Phone and one ID document.</span></span>
            </label>
          )}
          <label className={`flex cursor-pointer gap-3 rounded-lg p-3 ${level === 'Organizer' ? 'border-2 border-teal bg-teal-soft' : 'border border-line'}`}>
            <input type="radio" name="level" checked={level === 'Organizer'} onChange={() => setLevel('Organizer')} className="mt-1 accent-teal" />
            <span><span className="block font-semibold">{role === 'Member' ? 'Become an organizer' : 'Run groups'}</span><span className="text-sm text-ink-soft">Also your BVN and a passport photograph.</span></span>
          </label>
        </fieldset>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="ID document" error={fe(submit.error, 'idType')}>
            {(id) => (
              <Select id={id} value={idType} onChange={(e) => setIdType(e.target.value as IdDocumentType)}>
                {Object.entries(idTypeLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </Select>
            )}
          </Field>
          <Field label={idType === 'NationalId' ? 'NIN (11 digits)' : 'Document number'} error={fe(submit.error, 'idNumber')}>
            {(id, d) => <TextInput id={id} aria-describedby={d} autoComplete="off" value={idNumber} onChange={(e) => setIdNumber(e.target.value)} />}
          </Field>
          {level === 'Organizer' && (
            <Field label="BVN (11 digits)" hint="Never share your BVN by phone or chat — only here." error={fe(submit.error, 'bvn')}>
              {(id, d) => <TextInput id={id} aria-describedby={d} inputMode="numeric" autoComplete="off" maxLength={14} value={bvn} onChange={(e) => setBvn(e.target.value)} />}
            </Field>
          )}
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Front of the ID" hint="JPEG, PNG or PDF, under 5 MB." error={fe(submit.error, 'idFront')}>
            {(id, d) => <input id={id} aria-describedby={d} type="file" accept="image/jpeg,image/png,application/pdf" className={fileCls} onChange={pick('idFront')} />}
          </Field>
          <Field label="Back of the ID (if it has one)" error={fe(submit.error, 'idBack')}>
            {(id, d) => <input id={id} aria-describedby={d} type="file" accept="image/jpeg,image/png,application/pdf" className={fileCls} onChange={pick('idBack')} />}
          </Field>
          {level === 'Organizer' && (
            <Field label="Passport photograph" hint="A clear photo of your face, JPEG or PNG." error={fe(submit.error, 'passportPhoto')}>
              {(id, d) => <input id={id} aria-describedby={d} type="file" accept="image/jpeg,image/png" capture="user" className={fileCls} onChange={pick('passportPhoto')} />}
            </Field>
          )}
        </div>
        <label className="flex items-start gap-3 text-sm">
          <input type="checkbox" className="mt-1 size-4 accent-teal" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
          <span>I agree that the operations team may check these details to verify my identity, as described in the <Link to="/privacy" className="font-semibold underline">Privacy Notice</Link>.</span>
        </label>
        {submit.error && !(submit.error instanceof ApiError && submit.error.status === 400) && <ErrorAlert error={submit.error} />}
        {submit.error instanceof ApiError && submit.error.status === 400 && <Alert tone="danger" title="Please fix the highlighted fields." />}
        <Button type="submit" busy={submit.isPending} disabled={!consent || !files.idFront || !idNumber.trim()}>Submit for review</Button>
      </form>
    </Card>
  )
}
