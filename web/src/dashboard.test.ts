import assert from 'node:assert/strict'
import { test } from 'node:test'
import { maxAbs, pct } from './dashboardmath.ts'

test('maxAbs and pct compare decimal strings exactly, negatives by magnitude', () => {
  assert.equal(maxAbs(['10', '-250.5', '99.9999']), '250.5')
  assert.equal(maxAbs([]), '0')
  assert.equal(pct('50', '200'), 25)
  assert.equal(pct('-200', '200'), 100)
  assert.equal(pct('5', '0'), 0)
})
