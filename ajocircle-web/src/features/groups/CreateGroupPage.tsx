import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../components/Layout'
import { Alert, Button, Card, ErrorAlert, Field, Select, TextInput } from '../../components/ui'
import { useMe } from '../../hooks/useMe'
import { api, ApiError } from '../../lib/api'
import { addDays, lagosToday, parseNairaToKobo } from '../../lib/format'
import type { Frequency, GroupDetail, PayoutOrderMethod, RotatingRules, SchemePreview } from '../../types/api'
import { ScheduleTable, SchemeTerms } from '../schemes/SchemeTerms'

type Draft = { name: string; amount: string; frequency: Frequency; memberCount: string; startDate: string; payoutOffsetDays: string; payoutOrder: PayoutOrderMethod }

const steps = ['Scheme', 'Terms', 'Review'] as const

export function CreateGroupPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: me } = useMe()
  const [step, setStep] = useState(0)
  const [d, setD] = useState<Draft>({
    name: '', amount: '', frequency: 'Monthly', memberCount: '10', startDate: addDays(lagosToday(), 14), payoutOffsetDays: '0', payoutOrder: 'JoinOrder',
  })
  const set = (patch: Partial<Draft>) => setD((x) => ({ ...x, ...patch }))

  const kobo = parseNairaToKobo(d.amount)
  const rules: RotatingRules = {
    contributionAmountMinor: kobo ?? 0, currency: 'NGN', frequency: d.frequency, memberCount: Number(d.memberCount) || 0,
    startDate: d.startDate, payoutOffsetDays: Number(d.payoutOffsetDays) || 0, payoutOrder: d.payoutOrder,
  }

  // Server-side preview is authoritative for totals and dates (brief §10.4).
  const preview = useMutation({ mutationFn: () => api.post<SchemePreview>('/schemes/rotating/preview', rules), onSuccess: () => setStep(2) })
  const create = useMutation({
    mutationFn: () => api.post<GroupDetail>('/groups', { name: d.name, rules }),
    onSuccess: (g) => { qc.invalidateQueries({ queryKey: ['groups'] }); navigate(`/app/groups/${g.id}`) },
  })
  const fe = (preview.error ?? create.error) instanceof ApiError ? ((preview.error ?? create.error) as ApiError).fieldErrors : {}

  const step1Valid = d.name.trim().length >= 3
  const step2Valid = kobo !== null && kobo > 0 && Number(d.memberCount) >= 2 && !!d.startDate

  return (
    <>
      <PageHeader title="New group" back={{ to: '/app', label: 'My groups' }} />
      <ol className="mb-5 flex gap-2 text-xs font-semibold" aria-label="Progress">
        {steps.map((s, i) => (
          <li key={s} aria-current={i === step ? 'step' : undefined}
            className={`flex-1 rounded-full px-3 py-1.5 text-center ${i === step ? 'bg-ink text-white' : i < step ? 'bg-ink/10 text-ink' : 'bg-card text-ink-soft border border-line'}`}>
            {i + 1}. {s}
          </li>
        ))}
      </ol>

      {me && !me.contactVerified && <div className="mb-4"><Alert tone="warn">You can plan a group now, but you must verify your email before saving it.</Alert></div>}

      {step === 0 && (
        <Card className="space-y-5">
          <fieldset>
            <legend className="mb-2 text-sm font-semibold">Scheme type</legend>
            <div className="grid gap-2 sm:grid-cols-2">
              <label className="flex cursor-pointer gap-3 rounded-xl border-2 border-brand bg-brand-soft p-3">
                <input type="radio" name="scheme" defaultChecked className="mt-1 accent-brand" />
                <span><span className="block font-semibold">Rotating ajo</span><span className="text-sm text-ink-soft">Everyone contributes each round; one member receives the pool per round.</span></span>
              </label>
              <div className="flex gap-3 rounded-xl border border-line p-3 opacity-60" aria-disabled="true">
                <input type="radio" name="scheme" disabled className="mt-1" />
                <span><span className="block font-semibold">Fixed-term savings</span><span className="text-sm text-ink-soft">Individual or shared targets. Coming in the next release.</span></span>
              </div>
            </div>
          </fieldset>
          <Field label="Group name" hint="Members see this name." error={fe.name}>
            {(id, desc) => <TextInput id={id} aria-describedby={desc} maxLength={80} value={d.name} onChange={(e) => set({ name: e.target.value })} placeholder="e.g. Balogun Market Monthly Ajo" />}
          </Field>
          <div className="flex justify-end"><Button disabled={!step1Valid} onClick={() => setStep(1)}>Continue</Button></div>
        </Card>
      )}

      {step === 1 && (
        <Card className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Contribution per member per round (₦)" hint="Whole naira, e.g. 100,000" error={fe.contributionAmountMinor}>
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
            <Field label="Number of members" hint="Each member gets one payout, so this is also the number of rounds." error={fe.memberCount}>
              {(id, desc) => <TextInput id={id} aria-describedby={desc} type="number" min={2} max={100} inputMode="numeric" value={d.memberCount} onChange={(e) => set({ memberCount: e.target.value })} />}
            </Field>
            <Field label="First contribution due date" error={fe.startDate}>
              {(id) => <TextInput id={id} type="date" min={lagosToday()} value={d.startDate} onChange={(e) => set({ startDate: e.target.value })} />}
            </Field>
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
          </div>
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
            <SchemeTerms rules={rules} summary={preview.data.summary} />
          </Card>
          <Card>
            <h2 className="mb-3 font-bold">Schedule</h2>
            <ScheduleTable schedule={preview.data.schedule} />
          </Card>
          <ErrorAlert error={create.error} />
          <div className="flex justify-between gap-2">
            <Button variant="secondary" onClick={() => setStep(1)}>Edit terms</Button>
            <Button busy={create.isPending} disabled={!me?.contactVerified} onClick={() => create.mutate()}>Save draft</Button>
          </div>
        </div>
      )}
    </>
  )
}
