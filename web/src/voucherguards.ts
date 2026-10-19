// Which vouchers the UI may re-enter through a form without losing information the form cannot carry.
import type { Voucher } from './api'

export const CASH_TYPES = ['CR', 'CP', 'BR', 'BP']
const EVENTS_BY_TYPE: Record<string, string[]> = {
  CR: ['CUSTOMER_RECEIPT'], BR: ['CUSTOMER_RECEIPT'], CP: ['EXPENSE', 'SUPPLIER_PAYMENT'], BP: ['SUPPLIER_PAYMENT', 'INTERNAL_TRANSFER'],
}

/** Cash/bank documents made by the Thu/Chi form (known business event, functional currency, no import source) can be edited there; others (FX payments, imports…) cannot. */
export function canEditAsCashDoc(v: Pick<Voucher, 'voucherType' | 'businessEvent' | 'sourceSystem' | 'lines'>): boolean {
  return !!v.businessEvent && (EVENTS_BY_TYPE[v.voucherType] ?? []).includes(v.businessEvent) && !v.sourceSystem && !v.lines.some(l => l.foreignCurrency)
}

/** Copying through the journal-voucher form drops foreign-currency amounts, so such vouchers are not copyable. */
export function canCopyVoucher(v: Pick<Voucher, 'voucherType' | 'lines'>): boolean {
  return ['GJ', 'ADJ', 'ALC', 'OPB', 'CLS', 'FXR'].includes(v.voucherType) && !v.lines.some(l => l.foreignCurrency)
}
