import type { Frequency, Money } from '../types/api'

const naira = new Intl.NumberFormat('en-NG', { style: 'currency', currency: 'NGN', currencyDisplay: 'symbol', minimumFractionDigits: 0, maximumFractionDigits: 2 })

/** Formats integer kobo as ₦ — display only; never do money maths with floats. */
export function formatMoney(m: Money | number): string {
  const minor = typeof m === 'number' ? m : m.amountMinor
  // Integer division keeps whole-naira display exact; kobo shown only when present.
  const whole = Math.trunc(minor / 100)
  const kobo = Math.abs(minor % 100)
  return kobo === 0 ? naira.format(whole) : naira.format(whole + kobo / 100)
}

/** Parses a user-typed naira amount ("100,000") to integer kobo, or null if invalid. Whole naira only. */
export function parseNairaToKobo(input: string): number | null {
  const cleaned = input.replace(/[₦,\s]/g, '')
  if (!/^\d{1,9}$/.test(cleaned)) return null
  return Number(cleaned) * 100
}

const dateFmt = new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
const dateTimeFmt = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Lagos' })

/** Calendar date (yyyy-mm-dd) → "Tue, 1 Dec 2026". Unambiguous, no dd/mm vs mm/dd confusion. */
export function formatDate(isoDate: string): string {
  return dateFmt.format(new Date(isoDate + 'T00:00:00Z'))
}

/** UTC timestamp → Lagos local time. */
export function formatDateTime(iso: string): string {
  return dateTimeFmt.format(new Date(iso)) + ' WAT'
}

export const frequencyLabel: Record<Frequency, string> = { Weekly: 'Weekly', Biweekly: 'Every 2 weeks', Monthly: 'Monthly' }
export const frequencyNoun: Record<Frequency, string> = { Weekly: 'week', Biweekly: 'fortnight', Monthly: 'month' }

/** Today's date in Lagos as yyyy-mm-dd. */
export function lagosToday(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Lagos' }).format(new Date())
}

export function addDays(isoDate: string, days: number): string {
  const d = new Date(isoDate + 'T00:00:00Z')
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}
