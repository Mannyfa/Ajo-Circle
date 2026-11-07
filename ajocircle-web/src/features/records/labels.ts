import type { DisputeCategory, PaymentMethod } from '../../types/api'

export const methodLabel: Record<PaymentMethod, string> = {
  Cash: 'Cash', BankTransfer: 'Bank transfer', PosOrMobileMoney: 'POS / mobile money', Other: 'Other',
}

export const categoryLabel: Record<DisputeCategory, string> = {
  DidNotHappen: 'This payment didn’t happen',
  WrongAmount: 'The amount is wrong',
  WrongDate: 'The date is wrong',
  Other: 'Something else',
}
