import assert from 'node:assert/strict'
import { test } from 'node:test'
import { canCopyVoucher, canEditAsCashDoc, canEditJournalVoucher, isModuleGenerated } from './voucherguards.ts'

const line = (foreignCurrency: string | null = null) => ({ lineNo: 1, accountId: 'a', side: 'D' as const, amount: '1', partnerId: null, description: null, foreignCurrency })

test('cash documents are editable in the Thu/Chi form only when it can represent them faithfully', () => {
  const base = { voucherType: 'CR', businessEvent: 'CUSTOMER_RECEIPT', sourceSystem: null, lines: [line()] }
  assert.equal(canEditAsCashDoc(base), true)
  assert.equal(canEditAsCashDoc({ ...base, businessEvent: null }), false)
  assert.equal(canEditAsCashDoc({ ...base, businessEvent: 'EXPENSE' }), false)
  assert.equal(canEditAsCashDoc({ ...base, sourceSystem: 'BANK_IMPORT' }), false)
  assert.equal(canEditAsCashDoc({ ...base, lines: [line('USD')] }), false)
})

test('voucher copy refuses foreign-currency lines and non-journal types', () => {
  assert.equal(canCopyVoucher({ voucherType: 'GJ', lines: [line()] }), true)
  assert.equal(canCopyVoucher({ voucherType: 'GJ', lines: [line('USD')] }), false)
  assert.equal(canCopyVoucher({ voucherType: 'CR', lines: [line()] }), false)
})

test('module-generated vouchers are recalculated in their module, never hand-edited; imported ones stay editable', () => {
  assert.equal(isModuleGenerated({ sourceSystem: null }), false)
  assert.equal(isModuleGenerated({ sourceSystem: 'HAK.IMPORT' }), false)
  for (const s of ['HAK.CLOSING', 'HAK.ADVANCE', 'HAK.EQUITY', 'HAK.INVOICE', 'HAK.VAT.OFFSET']) assert.equal(isModuleGenerated({ sourceSystem: s }), true, s)
  assert.equal(isModuleGenerated({ sourceSystem: 'BANK_IMPORT' }), false)
})

test('the plain journal form refuses drafts it would silently strip (foreign currency, due date, settlement links)', () => {
  const l = (extra: object) => ({ lineNo: 1, accountId: 'a', side: 'D' as const, amount: '1', partnerId: null, description: null, ...extra })
  assert.equal(canEditJournalVoucher({ lines: [l({})] }), true)
  assert.equal(canEditJournalVoucher({ lines: [l({ foreignCurrency: 'USD' })] }), false)
  assert.equal(canEditJournalVoucher({ lines: [l({ dueDate: '2026-12-31' })] }), false)
  assert.equal(canEditJournalVoucher({ lines: [l({ settleJournalLineId: 'x' })] }), false)
})
