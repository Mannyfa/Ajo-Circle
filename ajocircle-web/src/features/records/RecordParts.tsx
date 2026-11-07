import { useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { RecordStatusBadge } from '../../components/StatusBadge'
import { Alert, Button, Card, ErrorAlert, Field, Select, TextInput } from '../../components/ui'
import { ApiError } from '../../lib/api'
import { addDays, formatDate, formatDateTime, formatMoney, lagosToday, parseNairaToKobo } from '../../lib/format'
import type { DisputeCategory, Money, PaymentInput, PaymentMethod, PaymentRecord } from '../../types/api'
import { categoryLabel, methodLabel } from './labels'

const fieldErrors = (e: unknown, key: string) => (e instanceof ApiError ? e.fieldErrors[key] : undefined)

/**
 * Form for something that happened outside the app. When `fixedAmount` is set (contributions), the amount is shown,
 * not typed: MVP records must match what is owed. Otherwise the amount is editable up to `maxAmount` (payouts).
 */
export function PaymentForm({ fixedAmount, maxAmount, submitLabel, busy, error, onSubmit, onCancel, intro, initial }: {
  fixedAmount?: Money; maxAmount?: Money; submitLabel: string; busy: boolean; error: unknown
  onSubmit: (p: PaymentInput) => void; onCancel?: () => void; intro?: string
  /** Start from an existing record's values (corrections), so only the wrong field needs changing. */
  initial?: PaymentRecord
}) {
  const today = lagosToday()
  const [amount, setAmount] = useState(initial ? String(initial.amount.amountMinor / 100) : maxAmount ? String(maxAmount.amountMinor / 100) : '')
  const [paidOn, setPaidOn] = useState(initial?.paidOn ?? today)
  const [method, setMethod] = useState<PaymentMethod>(initial?.method ?? 'BankTransfer')
  const [evidence, setEvidence] = useState(initial?.evidenceReference ?? '')
  const [note, setNote] = useState(initial?.note ?? '')
  const [localError, setLocalError] = useState<string | null>(null)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const amountMinor = fixedAmount ? fixedAmount.amountMinor : parseNairaToKobo(amount)
    if (amountMinor === null || amountMinor <= 0) { setLocalError('Enter a whole-naira amount, e.g. 100000.'); return }
    if (maxAmount && amountMinor > maxAmount.amountMinor) { setLocalError(`The amount can’t be more than ${formatMoney(maxAmount)}.`); return }
    setLocalError(null)
    onSubmit({ amountMinor, paidOn, method, evidenceReference: evidence.trim() || null, note: note.trim() || null })
  }

  return (
    <form className="space-y-3" onSubmit={submit} noValidate>
      {intro && <p className="text-sm text-ink-soft">{intro}</p>}
      {fixedAmount ? (
        <div className="rounded-xl bg-paper px-3 py-2 text-sm">
          Amount <strong className="tabular-nums">{formatMoney(fixedAmount)}</strong>
          <span className="block text-xs text-ink-soft">The full contribution. Part payments aren’t supported yet.</span>
        </div>
      ) : (
        <Field label="Amount paid (₦)" hint={maxAmount ? `Up to ${formatMoney(maxAmount)}, the scheduled payout.` : undefined} error={fieldErrors(error, 'amountMinor')}>
          {(id, d) => <TextInput id={id} aria-describedby={d} inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
      )}
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Date paid" error={fieldErrors(error, 'paidOn')}>
          {(id, d) => <TextInput id={id} aria-describedby={d} type="date" value={paidOn} max={today} min={addDays(today, -366)} onChange={(e) => setPaidOn(e.target.value)} />}
        </Field>
        <Field label="How it was paid" error={fieldErrors(error, 'method')}>
          {(id, d) => (
            <Select id={id} aria-describedby={d} value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {Object.entries(methodLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
          )}
        </Field>
      </div>
      <Field label="Reference (optional)" hint="A transfer or receipt reference. Never enter account numbers, PINs or passwords." error={fieldErrors(error, 'evidenceReference')}>
        {(id, d) => <TextInput id={id} aria-describedby={d} value={evidence} maxLength={120} onChange={(e) => setEvidence(e.target.value)} />}
      </Field>
      <Field label="Note (optional)" error={fieldErrors(error, 'note')}>
        {(id, d) => <TextInput id={id} aria-describedby={d} value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />}
      </Field>
      {localError && <Alert tone="danger" title={localError} />}
      <ErrorAlert error={error} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" busy={busy}>{submitLabel}</Button>
        {onCancel && <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>}
      </div>
    </form>
  )
}

/** Every record, newest first. Reversed records stay visible with who reversed them and why (brief §4.5). */
export function RecordHistory({ records, payout, renderActions, hideDisputeLink }: {
  records: PaymentRecord[]; payout?: boolean; renderActions?: (r: PaymentRecord) => ReactNode; hideDisputeLink?: boolean
}) {
  return (
    <ol className="space-y-3">
      {records.map((r) => (
        <li key={r.id} className={`rounded-xl border p-3 ${r.status === 'Reversed' ? 'border-dashed border-line bg-paper/60' : 'border-line'}`}>
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div>
              <p className={`font-semibold tabular-nums ${r.status === 'Reversed' ? 'text-ink-soft line-through' : ''}`}>{formatMoney(r.amount)}</p>
              <p className="text-xs text-ink-soft">Paid {formatDate(r.paidOn)} · {methodLabel[r.method]}</p>
            </div>
            <RecordStatusBadge status={r.status} payout={payout} />
          </div>
          <dl className="mt-2 grid gap-x-4 gap-y-1 text-xs text-ink-soft sm:grid-cols-2">
            {r.evidenceReference && <div><dt className="inline">Reference: </dt><dd className="inline font-mono text-ink">{r.evidenceReference}</dd></div>}
            {r.note && <div className="sm:col-span-2"><dt className="inline">Note: </dt><dd className="inline text-ink">{r.note}</dd></div>}
            <div><dt className="inline">Recorded by </dt><dd className="inline">{r.recordedByName}, {formatDateTime(r.recordedAt)}</dd></div>
            {r.confirmedAt && <div><dt className="inline">Confirmed by </dt><dd className="inline">{r.confirmedByName}, {formatDateTime(r.confirmedAt)}</dd></div>}
            {r.reversedAt && <div className="sm:col-span-2"><dt className="inline">Reversed by </dt><dd className="inline">{r.reversedByName}, {formatDateTime(r.reversedAt)} — “{r.reversalReason}”</dd></div>}
            {r.replacesRecordId && <div className="sm:col-span-2"><dd>Corrects an earlier record.</dd></div>}
          </dl>
          {r.openDisputeId && !hideDisputeLink && <p className="mt-2 text-xs"><Link className="font-semibold underline" to={`/app/disputes/${r.openDisputeId}`}>View open dispute</Link></p>}
          {renderActions?.(r)}
        </li>
      ))}
    </ol>
  )
}

/** Organizer correction: either fix the details (reverse + replace) or reverse only. A reason is always required. */
export function CorrectionForm({ record, fixedAmount, maxAmount, busy, error, onSubmit, onCancel }: {
  record: PaymentRecord; fixedAmount?: Money; maxAmount?: Money; busy: boolean; error: unknown
  onSubmit: (reason: string, replacement: PaymentInput | null) => void; onCancel: () => void
}) {
  const [mode, setMode] = useState<'replace' | 'reverse'>('replace')
  const [reason, setReason] = useState('')
  const reasonOk = reason.trim().length >= 5
  return (
    <Card className="mt-3 space-y-3 border-tone-warn/40">
      <h3 className="font-bold">Correct this record</h3>
      <p className="text-sm text-ink-soft">The record of {formatMoney(record.amount)} stays visible and is marked reversed. {mode === 'replace' ? 'Your corrected details become a new record that the member can confirm or dispute.' : 'Nothing replaces it, so the item goes back to unpaid.'}</p>
      {record.openDisputeId && <Alert tone="info">This also resolves the open dispute in the member’s favour.</Alert>}
      <fieldset className="flex flex-wrap gap-2">
        <legend className="sr-only">Type of correction</legend>
        {(['replace', 'reverse'] as const).map((m) => (
          <label key={m} className={`flex min-h-11 cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm font-semibold ${mode === m ? 'border-ink bg-ink/5' : 'border-line'}`}>
            <input type="radio" name="mode" value={m} checked={mode === m} onChange={() => setMode(m)} />
            {m === 'replace' ? 'Fix the details' : 'Reverse — it didn’t happen'}
          </label>
        ))}
      </fieldset>
      <Field label="Reason for the correction" hint="Required. The member and the activity log will see it." error={fieldErrors(error, 'reason')}>
        {(id, d) => <TextInput id={id} aria-describedby={d} value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />}
      </Field>
      {mode === 'replace' ? (
        <PaymentForm fixedAmount={fixedAmount} maxAmount={maxAmount} submitLabel="Save correction" busy={busy} error={error}
          onSubmit={(p) => onSubmit(reason, p)} onCancel={onCancel} intro="Change whatever was wrong:" initial={record} />
      ) : (
        <>
          <ErrorAlert error={error} />
          <div className="flex flex-wrap gap-2">
            <Button variant="danger" busy={busy} disabled={!reasonOk} onClick={() => onSubmit(reason, null)}>Reverse record</Button>
            <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          </div>
        </>
      )}
    </Card>
  )
}

export function DisputeForm({ busy, error, onSubmit, onCancel }: {
  busy: boolean; error: unknown; onSubmit: (category: DisputeCategory, description: string) => void; onCancel: () => void
}) {
  const [category, setCategory] = useState<DisputeCategory>('WrongAmount')
  const [description, setDescription] = useState('')
  return (
    <form className="mt-3 space-y-3 rounded-xl border border-tone-danger/30 bg-tone-danger-bg/40 p-3" onSubmit={(e) => { e.preventDefault(); onSubmit(category, description) }}>
      <p className="text-sm">Only you and the organizer will see this. You’ll get a reference number to quote.</p>
      <Field label="What’s wrong?" error={fieldErrors(error, 'category')}>
        {(id, d) => (
          <Select id={id} aria-describedby={d} value={category} onChange={(e) => setCategory(e.target.value as DisputeCategory)}>
            {Object.entries(categoryLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </Select>
        )}
      </Field>
      <Field label="Explain what happened" hint="At least 10 characters." error={fieldErrors(error, 'description')}>
        {(id, d) => <textarea id={id} aria-describedby={d} rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)}
          className="block w-full rounded-xl border border-line bg-card px-3 py-2 text-base focus:border-brand focus:outline-none" />}
      </Field>
      <ErrorAlert error={error} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant="danger" busy={busy} disabled={description.trim().length < 10}>Raise dispute</Button>
        <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}
