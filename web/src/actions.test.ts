import test from 'node:test'
import assert from 'node:assert/strict'
import { refuseReason, withRowVersion } from './actions.ts'

test('thiếu quyền thì từ chối trước khi gọi máy chủ', () => {
  const r = refuseReason({ needs: 'vouchers.approve' }, 0, () => false)
  assert.equal(r?.reason, 'permission')
  assert.equal(refuseReason({ needs: 'vouchers.approve' }, 0, p => p === 'vouchers.approve'), null)
})
test('exclusive bỏ qua lần bấm khi đang chạy, không exclusive thì cho chạy song song', () => {
  assert.equal(refuseReason({ exclusive: true }, 1, () => true)?.reason, 'busy')
  assert.equal(refuseReason({ exclusive: true }, 0, () => true), null)
  assert.equal(refuseReason(undefined, 3, () => true), null)
})
test('withRowVersion gắn đúng dấu ? hoặc &, không gắn hai lần', () => {
  assert.equal(withRowVersion('/vouchers/1/post', 7), '/vouchers/1/post?rowVersion=7')
  assert.equal(withRowVersion('/x?a=1', '9'), '/x?a=1&rowVersion=9')
  assert.throws(() => withRowVersion('/x?rowVersion=1', 2))
})
