import test from 'node:test'
import assert from 'node:assert/strict'
import { newIdempotencyKey } from './api.ts'

test('newIdempotencyKey gives distinct keys of 8-100 safe characters', () => {
  const keys = new Set(Array.from({ length: 200 }, () => newIdempotencyKey()))
  assert.equal(keys.size, 200)
  for (const k of keys) assert.match(k, /^[A-Za-z0-9_-]{8,100}$/)
})
