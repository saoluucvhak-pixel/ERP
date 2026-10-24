import { test } from 'node:test'
import assert from 'node:assert/strict'
import { confirmLeave, hasUnsavedWork } from './unsaved.ts'

test('without unsaved work, leaving is allowed without asking', () => {
  assert.equal(hasUnsavedWork(), false)
  let asked = false
  ;(globalThis as { window?: unknown }).window = { confirm: () => { asked = true; return false } }
  assert.equal(confirmLeave(), true)
  assert.equal(asked, false)
})
