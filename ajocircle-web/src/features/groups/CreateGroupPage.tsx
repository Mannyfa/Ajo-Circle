import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { Alert, Button, Card, ErrorAlert, Field, Select, TextInput } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api, ApiError } from '../../lib/api'
import { addDays, lagosToday, parseNairaToKobo } from '../../lib/format'
import type { FixedTermRules, Frequency, GroupDetail, PayoutOrderMethod, RotatingRules, SavingsAllocation, SchemePreview } from '../../types/api'
import { allocationLabel } from '../schemes/labels'
import { FixedTermTerms, ScheduleTable, SchemeTerms } from '../schemes/SchemeTerms'

type Kind = 'rotating' | 'individual' | 'shared'
type Draft = {
  name: string; amount: string; frequency: Frequency; memberCount: string; startDate: string
  payoutOffsetDays: string; payoutOrder: PayoutOrderMethod
  periods: string; releaseOffsetDays: string; allocation: SavingsAllocation; goal: string
}

const steps = ['Scheme', 'Terms', 'Review'] as const

const kinds: { value: Kind; title: string; text: string }[] = [
  { value: 'rotating', title: 'Rotating ajo', text: 'Everyone contributes each round; one member receives the pool per round.' },
  { value: 'individual', title: 'Individual savings', text: 'Each member saves towards their own target and gets back what they paid.' },
  { value: 'shared', title: 'Shared savings', text: 'Members save towards one goal; you choose how the total is shared at the end.' },
]

export function CreateGroupPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: me } = useMe()
  const [step, setStep] = useState(0)
  const [kind, setKind] = useState<Kind>('rotating')
  const [d, setD] = useState<Draft>({
    name: '', amount: '', frequency: 'Monthly', memberCount: '10', startDate: addDays(lagosToday(), 14), payoutOffsetDays: '0', payoutOrder: 'JoinOrder',
    periods: '6', releaseOffsetDays: '0', allocation: 'Equal', goal: '',
  })
  const set = (patch: Partial<Draft>) => setD((x) => ({ ...x, ...patch }))
  const fixed = kind !== 'rotating'

  const kobo = parseNairaToKobo(d.amount)
  const rotating: RotatingRules = {
    contributionAmountMinor: kobo ?? 0, currency: 'NGN', frequency: d.frequency, memberCount: Number(d.memberCount) || 0,
    startDate: d.startDate, payoutOffsetDays: Number(d.payoutOffsetDays) || 0, payoutOrder: d.payoutOrder,
  }
  const fixedTerm: FixedTermRules = {
    variant: kind === 'shared' ? 'Shared' : 'Individual', contributionAmountMinor: kobo ?? 0, currency: 'NGN', frequency: d.frequency,
    memberCount: Number(d.memberCount) || 0, periods: Number(d.periods) || 0, startDate: d.startDate,
    releaseOffsetDays: Number(d.releaseOffsetDays) || 0, allocation: kind === 'shared' ? d.allocation : 'OwnContributions', goal: d.goal.trim() || null,
  }

  // Server-side preview is authoritative for totals and dates (brief §10.4).
  const preview = useMutation({
    mutationFn: () => fixed ? api.post<SchemePreview>('/schemes/fixed-term/preview', fixedTerm) : api.post<SchemePreview>('/schemes/rotating/preview', rotating),
    onSuccess: () => setStep(2),
  })
  const create = useMutation({
    mutationFn: () => fixed
      ? api.post<GroupDetail>('/groups/fixed-term', { name: d.name, rules: fixedTerm })
      : api.post<GroupDetail>('/groups', { name: d.name, rules: rotating }),
    onSuccess: (g) => { qc.invalidateQueries({ queryKey: ['groups'] }); navigate(`/app/groups/${g.id}`) },
  })
  const err = preview.error ?? create.error
  const fe = err instanceof ApiError ? err.fieldErrors : {}

  const step1Valid = d.name.trim().length >= 3
  const step2Valid = kobo !== null && kobo > 0 && Number(d.memberCount) >= 2 && !!d.startDate && (!fixed || Number(d.periods) >= 1)

  return (
    <>
      <PageHeader title="New group" back={{ to: '/app/groups', label: 'My groups' }} />
      <ol className="mb-5 flex gap-2 text-xs font-semibold" aria-label="Progress">
        {steps.map((s, i) => (
          <li key={s} aria-current={i === step ? 'step' : undefined}
            className={`flex-1 rounded-full px-3 py-1.5 text-center ${i === step ? 'bg-brand text-white' : i < step ? 'bg-ink/10 text-ink' : 'bg-card text-ink-soft border border-line'}`}>
            {i + 1}. {s}
          </li>
        ))}
      </ol>

      {me && me.identityLevel !== 'Organizer' && (
        <div className="mb-4">
          <Alert tone="warn" title="Organizers verify their identity first">
            You can plan a group now, but to save it you need to verify as an organizer (ID, BVN, phone and passport photo).{' '}
            <Link to="/app/verification" className="font-semibold underline">Start verification</Link>
          </Alert>
        </div>
      )}

      {step === 0 && (
        <Card className="space-y-5">
          <fieldset>
            <legend className="mb-2 text-sm font-semibold">Scheme type</legend>
            <div className="grid gap-2 sm:grid-cols-3">
              {kinds.map((k) => (
                <label key={k.value} className={`flex cursor-pointer gap-3 rounded-lg p-3 ${kind === k.value ? 'border-2 border-brand bg-brand-soft' : 'border border-line'}`}>
                  <input type="radio" name="scheme" value={k.value} checked={kind === k.value} onChange={() => { setKind(k.value); preview.reset() }} className="mt-1 accent-teal" />
                  <span><span className="block font-semibold">{k.title}</span><span className="text-sm text-ink-soft">{k.text}</span></span>
                </label>
              ))}
            </div>
          </fieldset>
          <Field label="Group name" hint="Members see this name." error={fe.name}>
            {(id, desc) => <TextInput id={id} aria-describedby={desc} maxLength={80} value={d.name} onChange={(e) => set({ name: e.target.value })}
              placeholder={fixed ? 'e.g. Lekki Christmas Savers' : 'e.g. Balogun Market Monthly Ajo'} />}
          </Field>
          <div className="flex justify-end"><Button disabled={!step1Valid} onClick={() => setStep(1)}>Continue</Button></div>
        </Card>
      )}

      {step === 1 && (
        <Card className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={fixed ? 'Contribution per member each time (₦)' : 'Contribution per member per round (₦)'} hint="Whole naira, e.g. 100,000" error={fe.contributionAmountMinor}>
              {(id, desc) => <TextInput id={id} aria-describedby={desc} inputMode="numeric" value={d.amount} onChange={(e) => set({ amount: e.target.value })} placeholder="100,000" aria-invalid={d.amount !== '' && kobo === null} />}
            </Field>
            <Field label="How often" error={fe.frequency}>
              {(id) => (
                <Select id={id} value={d.frequency} onChange={(e) => set({ frequency: e.target.value as Frequency })}>
                  <option value="Weekly">Weekly</option>
                  <option value="Biweekly">Every 2 weeks</option>
                  <option value="Monthly">Monthly</option>
                </Select>
              )}
            </Field>
            {fixed ? (
              <>
                <Field label="Number of contributions" hint="How many times each member pays." error={fe.periods}>
                  {(id, desc) => <TextInput id={id} aria-describedby={desc} type="number" min={1} max={104} inputMode="numeric" value={d.periods} onChange={(e) => set({ periods: e.target.value })} />}
                </Field>
                <Field label="Most members" hint="The group can start once at least 2 have joined." error={fe.memberCount}>
                  {(id, desc) => <TextInput id={id} aria-describedby={desc} type="number" min={2} max={100} inputMode="numeric" value={d.memberCount} onChange={(e) => set({ memberCount: e.target.value })} />}
                </Field>
              </>
            ) : (
              <Field label="Number of members" hint="Each member gets one payout, so this is also the number of rounds." error={fe.memberCount}>
                {(id, desc) => <TextInput id={id} aria-describedby={desc} type="number" min={2} max={100} inputMode="numeric" value={d.memberCount} onChange={(e) => set({ memberCount: e.target.value })} />}
              </Field>
            )}
            <Field label="First contribution due date" error={fe.startDate}>
              {(id) => <TextInput id={id} type="date" min={lagosToday()} value={d.startDate} onChange={(e) => set({ startDate: e.target.value })} />}
            </Field>
            {fixed ? (
              <>
                <Field label="When the money is released" hint="Days after the last contribution." error={fe.releaseOffsetDays}>
                  {(id, desc) => (
                    <Select id={id} aria-describedby={desc} value={d.releaseOffsetDays} onChange={(e) => set({ releaseOffsetDays: e.target.value })}>
                      {[0, 1, 2, 3, 5, 7, 14].map((n) => <option key={n} value={n}>{n === 0 ? 'Same day as the last contribution' : `${n} day${n > 1 ? 's' : ''} later`}</option>)}
                    </Select>
                  )}
                </Field>
                {kind === 'shared' && (
                  <Field label="How the total is shared at the end" hint="Members see and accept this before joining. It can’t change later." error={fe.allocation}>
                    {(id, desc) => (
                      <Select id={id} aria-describedby={desc} value={d.allocation} onChange={(e) => set({ allocation: e.target.value as SavingsAllocation })}>
                        {(['Equal', 'Proportional', 'OrganizerAssigned'] as const).map((a) => <option key={a} value={a}>{allocationLabel[a]}</option>)}
                      </Select>
                    )}
                  </Field>
                )}
                <Field label="Goal (optional)" hint="What members are saving for." error={fe.goal}>
                  {(id, desc) => <TextInput id={id} aria-describedby={desc} maxLength={120} value={d.goal} onChange={(e) => set({ goal: e.target.value })} placeholder="e.g. School fees for January" />}
                </Field>
              </>
            ) : (
              <>
                <Field label="Payout timing" hint="Days after contributions are due that the round’s payout is scheduled." error={fe.payoutOffsetDays}>
                  {(id, desc) => (
                    <Select id={id} aria-describedby={desc} value={d.payoutOffsetDays} onChange={(e) => set({ payoutOffsetDays: e.target.value })}>
                      {[0, 1, 2, 3, 4, 5, 6].map((n) => <option key={n} value={n}>{n === 0 ? 'Same day' : `${n} day${n > 1 ? 's' : ''} later`}</option>)}
                    </Select>
                  )}
                </Field>
                <Field label="Payout order" error={fe.payoutOrder}>
                  {(id) => (
                    <Select id={id} value={d.payoutOrder} onChange={(e) => set({ payoutOrder: e.target.value as PayoutOrderMethod })}>
                      <option value="JoinOrder">Order members join</option>
                      <option value="OrganizerAssigned">I’ll assign it before starting</option>
                    </Select>
                  )}
                </Field>
              </>
            )}
          </div>
          {fixed && (
            <Alert tone="neutral" title="Rules every savings group follows">
              You hold the money, not the app. A member who leaves early gets back what they paid; if the group is cancelled, everyone gets back what they paid. Part payments aren’t accepted.
            </Alert>
          )}
          {preview.error && !(preview.error instanceof ApiError && preview.error.status === 400) && <ErrorAlert error={preview.error} />}
          {preview.error instanceof ApiError && preview.error.status === 400 && <Alert tone="danger" title="Please fix the highlighted fields." />}
          <div className="flex justify-between gap-2">
            <Button variant="secondary" onClick={() => setStep(0)}>Back</Button>
            <Button disabled={!step2Valid} busy={preview.isPending} onClick={() => preview.mutate()}>Preview terms</Button>
          </div>
        </Card>
      )}

      {step === 2 && preview.data && (
        <div className="space-y-4">
          <Card>
            <h2 className="mb-1 text-lg font-bold">{d.name}</h2>
            <p className="mb-4 text-sm text-ink-soft">These are the terms members will see and accept. After you publish, they can’t be changed without an amendment.</p>
            {fixed && preview.data.fixedTermSummary
              ? <FixedTermTerms rules={fixedTerm} summary={preview.data.fixedTermSummary} />
              : preview.data.summary && <SchemeTerms rules={rotating} summary={preview.data.summary} />}
          </Card>
          <Card>
            <h2 className="mb-3 font-bold">Schedule</h2>
            <ScheduleTable schedule={preview.data.schedule} />
          </Card>
          <ErrorAlert error={create.error} />
          <div className="flex justify-between gap-2">
            <Button variant="secondary" onClick={() => setStep(1)}>Edit terms</Button>
            <Button busy={create.isPending} disabled={me?.identityLevel !== 'Organizer'} onClick={() => create.mutate()}>Save draft</Button>
          </div>
        </div>
      )}
    </>
  )
}
