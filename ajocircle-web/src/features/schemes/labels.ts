import type { PayoutKind, SavingsAllocation, SchemeType } from '../../types/api'

export const schemeLabel: Record<SchemeType, string> = {
  RotatingAjo: 'Rotating ajo',
  FixedTermIndividual: 'Individual savings',
  FixedTermShared: 'Shared savings',
}

/** How the money is shared out at the end. Locked into the terms members accept. */
export const allocationLabel: Record<SavingsAllocation, string> = {
  OwnContributions: 'Each member gets back what they paid',
  Equal: 'Split equally among members still in the group',
  Proportional: 'Shared in proportion to what each member paid',
  OrganizerAssigned: 'The organizer assigns each member a percentage before the start',
}

export const payoutKindLabel: Record<PayoutKind, string> = {
  RotatingPayout: 'Payout',
  Release: 'Savings release',
  ExitRefund: 'Refund for leaving early',
  CancellationRefund: 'Cancellation refund',
}

export const formatShare = (basisPoints: number) => `${(basisPoints / 100).toLocaleString('en-NG', { maximumFractionDigits: 2 })}%`
