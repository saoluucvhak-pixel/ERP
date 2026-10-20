import { test } from 'node:test'
import assert from 'node:assert/strict'
import { fold, matchesQuery, parseAmount, parseQuery } from './filter.ts'

test('fold bỏ dấu và hoa thường', () => {
  assert.equal(fold('Khách Hàng Đại Hiệp'), 'khach hang dai hiep')
})

test('nhiều từ phải cùng có (và), không phụ thuộc dấu', () => {
  assert.ok(matchesQuery('CTR-SAL-2026-00001 Hợp đồng dăm gỗ Khách hàng A', null, 'dam go khach'))
  assert.ok(!matchesQuery('CTR-SAL-2026-00001 Hợp đồng dăm gỗ', null, 'dam giay'))
})

test('cụm trong ngoặc kép giữ nguyên thứ tự', () => {
  assert.ok(matchesQuery('Máy nghiền dăm', null, '"nghien dam"'))
  assert.ok(!matchesQuery('Máy nghiền dăm', null, '"dam nghien"'))
})

test('điều kiện số tiền với đơn vị k, tr, ty', () => {
  assert.deepEqual(parseAmount('>500tr'), { op: '>', value: 500_000_000 })
  assert.deepEqual(parseAmount('<=2ty'), { op: '<=', value: 2_000_000_000 })
  assert.deepEqual(parseAmount('>=1,5tr'), { op: '>=', value: 1_500_000 })
  assert.equal(parseAmount('500tr'), null)
  assert.ok(matchesQuery('hợp đồng', 1_800_000_000, '>1ty hop dong'))
  assert.ok(!matchesQuery('hợp đồng', 900_000_000, '>1ty'))
  assert.ok(!matchesQuery('hợp đồng', null, '>1ty'), 'không có số tiền thì không khớp điều kiện số')
})

test('truy vấn rỗng khớp tất cả', () => {
  assert.ok(matchesQuery('bất kỳ', null, '   '))
  assert.deepEqual(parseQuery(''), { words: [], amounts: [] })
})
