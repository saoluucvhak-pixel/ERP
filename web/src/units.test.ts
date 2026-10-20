import assert from 'node:assert/strict'
import { test } from 'node:test'
import { toAlt, toMain } from './units.ts'

test('quy đổi đơn vị cố định bằng chuỗi thập phân, không dùng số thực', () => {
  assert.equal(toAlt('2000', '1000'), '2')
  assert.equal(toMain('12', '1000'), '12000')
  assert.equal(toAlt('1', '3'), '0.3333')
  assert.equal(toMain('0.3333', '3'), '0.9999')
  assert.equal(toAlt('-1500', '1000'), '-1.5')
  assert.equal(toAlt('5', '0'), null)
  assert.equal(toMain('abc', '2'), null)
})
