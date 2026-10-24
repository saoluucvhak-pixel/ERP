import { test } from 'node:test'
import assert from 'node:assert/strict'
import { QUICK_GROUPS } from './quickadd.ts'

const all = QUICK_GROUPS.flatMap(g => g.items)
const find = (label: string) => all.find(i => i.label === label)!

test('Thêm nhanh: Thu tiền và Chi tiền mở hai loại phiếu khác nhau (không dựa vào mặc định của màn)', () => {
  assert.deepEqual(find('Thu tiền').intent, { voucherType: 'CR', businessEvent: 'CUSTOMER_RECEIPT' })
  assert.deepEqual(find('Chi tiền').intent, { voucherType: 'CP', businessEvent: 'EXPENSE' })
})

test('Thêm nhanh: mua/bán cùng màn nhưng khác chiều', () => {
  for (const [label, screen, kind] of [['Đơn mua hàng', 'orders', 'Purchase'], ['Đơn bán hàng', 'orders', 'Sales'], ['Hóa đơn mua', 'trade', 'Purchase'], ['Hóa đơn bán', 'trade', 'Sales']] as const)
    assert.deepEqual([find(label).screen, find(label).kind], [screen, kind], label)
})

test('Thêm nhanh: Nhập kho / Xuất kho / Kiểm kê khác nhau', () => {
  assert.equal(find('Nhập kho').intent?.stockType, 'Receipt')
  assert.equal(find('Xuất kho').intent?.stockType, 'Issue')
  assert.equal(find('Kiểm kê').intent?.stockTab, 'count')
})

test('Thêm nhanh: mọi mục cùng màn đều phân biệt được bằng kind hoặc intent', () => {
  const sig = (i: (typeof all)[number]) => `${i.screen}|${i.kind ?? ''}|${JSON.stringify(i.intent ?? {})}`
  assert.equal(new Set(all.map(sig)).size, all.length)
})
