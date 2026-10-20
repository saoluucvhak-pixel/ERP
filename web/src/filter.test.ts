import { test } from 'node:test'
import assert from 'node:assert/strict'
import { datePresets, fold, inRange, matchesQuery, overlaps, parseAmount, parseQuery } from './filter.ts'

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

test('mốc ngày nhanh theo lịch địa phương (20/10/2026 là thứ Ba)', () => {
  const p = Object.fromEntries(datePresets(new Date(2026, 9, 20)).map(x => [x.key, x]))
  assert.deepEqual([p.today.from, p.today.to], ['2026-10-20', '2026-10-20'])
  assert.deepEqual([p.week.from, p.week.to], ['2026-10-19', '2026-10-20'])
  assert.deepEqual([p.month.from, p.month.to], ['2026-10-01', '2026-10-20'])
  assert.deepEqual([p.lastmonth.from, p.lastmonth.to], ['2026-09-01', '2026-09-30'])
  assert.deepEqual([p.quarter.from, p.quarter.to], ['2026-10-01', '2026-10-20'])
  assert.deepEqual([p.year.from, p.year.to], ['2026-01-01', '2026-10-20'])
  assert.deepEqual([p.all.from, p.all.to], ['', ''])
})

test('tháng trước của tháng Giêng là tháng 12 năm trước', () => {
  const p = Object.fromEntries(datePresets(new Date(2027, 0, 5)).map(x => [x.key, x]))
  assert.deepEqual([p.lastmonth.from, p.lastmonth.to], ['2026-12-01', '2026-12-31'])
})

test('inRange: gồm cả hai đầu, bỏ trống một đầu là không giới hạn', () => {
  const r = { from: '2026-10-01', to: '2026-10-20' }
  assert.ok(inRange(r, '2026-10-01') && inRange(r, '2026-10-20') && inRange(r, '2026-10-19T08:00:00Z'))
  assert.ok(!inRange(r, '2026-09-30') && !inRange(r, '2026-10-21') && !inRange(r, null))
  assert.ok(inRange({ from: '', to: '' }, '1999-01-01'))
  assert.ok(inRange({ from: '2026-10-01', to: '' }, '2030-01-01'))
})

test('overlaps: khoảng hiệu lực giao với khoảng lọc', () => {
  const r = { from: '2026-10-01', to: '2026-10-20' }
  assert.ok(overlaps(r, '2026-01-01', '2026-12-31'))
  assert.ok(overlaps(r, '2026-10-20', '2026-11-30'))
  assert.ok(!overlaps(r, '2026-10-21', '2026-11-30'))
  assert.ok(!overlaps(r, '2026-01-01', '2026-09-30'))
})
