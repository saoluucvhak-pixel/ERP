import test from 'node:test'
import assert from 'node:assert/strict'

// F03 / F04 (kiểm tra 25/10/2026): nội dung gửi lại phải là đúng chuỗi đã chụp; nhiều thao tác chưa xác định không ghi đè nhau.
const offline = () => { throw new TypeError('response lost') }

test('F03: sửa đối tượng sau lần gửi đầu không đổi nội dung các lần gửi lại (cùng khóa, cùng byte)', async () => {
  const api = await import('./api.ts?f03-edit')
  api.session.setToken('fake-A'); api.session.setCompany('fake-company')
  const original = globalThis.fetch
  const calls: { body: string; key: string }[] = []
  const body = { name: 'original' }
  globalThis.fetch = (async (_u: string, init: RequestInit) => {
    calls.push({ body: init.body as string, key: (init.headers as Record<string, string>)['Idempotency-Key'] })
    if (calls.length === 1) { body.name = 'edited'; offline() }
    return new Response('{}')
  }) as typeof fetch
  try {
    await api.post('/partners', body)
    assert.equal(calls.length, 2)
    assert.equal(calls[0].key, calls[1].key)
    assert.equal(calls[1].body, calls[0].body, 'cùng khóa thì phải cùng nội dung')
    assert.equal(JSON.parse(calls[1].body).name, 'original')
  } finally { globalThis.fetch = original }
})

test('F03: thử lại an toàn sau khi chưa xác định gửi đúng nội dung + khóa của lần đầu dù đối tượng đã bị sửa', async () => {
  const api = await import('./api.ts?f03-safe')
  api.session.setToken('fake-A'); api.session.setCompany('fake-company')
  const original = globalThis.fetch
  const calls: { body: string; key: string }[] = []
  const body = { name: 'original' }
  let down = true
  globalThis.fetch = (async (_u: string, init: RequestInit) => {
    calls.push({ body: init.body as string, key: (init.headers as Record<string, string>)['Idempotency-Key'] })
    if (down) offline()
    return new Response('{}')
  }) as typeof fetch
  try {
    const err = await api.post('/partners', body).catch((e: unknown) => e)
    assert.equal((err as { code: string }).code, api.UNCERTAIN_CODE)
    body.name = 'edited-after-pending'; down = false
    assert.equal(await api.retryPendingWrite((err as InstanceType<typeof api.UncertainError>).opId), true)
    const last = calls.at(-1)!
    assert.equal(last.key, calls[0].key)
    assert.equal(last.body, calls[0].body)
    assert.equal(api.hasPendingWrite(), false, 'đã xác nhận thì không còn chờ')
  } finally { globalThis.fetch = original }
})

test('F04: hai thao tác chưa xác định ở hai công ty không ghi đè nhau; quay lại công ty cũ vẫn đối soát được', async () => {
  const api = await import('./api.ts?f04-two')
  api.session.setToken('fake-session'); api.session.setCompany('company-A')
  const original = globalThis.fetch
  const sent: { company: string; key: string; body: string }[] = []
  let down = true
  globalThis.fetch = (async (_u: string, init: RequestInit) => {
    const h = init.headers as Record<string, string>
    sent.push({ company: h['X-Company-Id'], key: h['Idempotency-Key'], body: init.body as string })
    if (down) offline()
    return new Response('{}')
  }) as typeof fetch
  try {
    const a = await api.post('/partners', { code: 'A' }).catch((e: unknown) => e) as InstanceType<typeof api.UncertainError>
    api.session.setCompany('company-B')
    assert.equal(api.hasPendingWrite(), false, 'thao tác của công ty A không hiện ở công ty B')
    const b = await api.post('/partners', { code: 'B' }).catch((e: unknown) => e) as InstanceType<typeof api.UncertainError>
    assert.notEqual(a.opId, b.opId)
    assert.equal(api.pendingWrites().length, 1)
    api.session.setCompany('company-A')
    assert.equal(api.hasPendingWrite(), true, 'thao tác của A không bị B ghi đè')
    down = false; sent.length = 0
    assert.equal(await api.retryPendingWrite(a.opId), true)
    assert.deepEqual(sent.map(s => [s.company, JSON.parse(s.body).code]), [['company-A', 'A']], 'chỉ gửi lại đúng thao tác của A, dưới công ty A')
    assert.equal(api.hasPendingWrite(), false)
    api.session.setCompany('company-B'); assert.equal(api.hasPendingWrite(), true, 'thao tác của B vẫn còn')
  } finally { globalThis.fetch = original }
})

test('F04: hai thao tác khác nhau trong cùng một công ty cũng giữ riêng từng cái', async () => {
  const api = await import('./api.ts?f04-same')
  api.session.setToken('fake-session'); api.session.setCompany('company-C')
  const original = globalThis.fetch
  globalThis.fetch = (async () => offline()) as typeof fetch
  try {
    await api.post('/partners', { code: 'X' }).catch(() => {})
    await api.post('/partners', { code: 'Y' }).catch(() => {})
    assert.equal(api.pendingWrites().length, 2)
    assert.deepEqual(api.pendingWrites().map(o => o.state), ['Uncertain', 'Uncertain'])
  } finally { globalThis.fetch = original }
})

test('thao tác bị máy chủ từ chối rõ ràng khi thử lại thì bị gỡ khỏi hàng chờ (Rejected), không treo mãi', async () => {
  const api = await import('./api.ts?f04-rejected')
  api.session.setToken('fake-session'); api.session.setCompany('company-D')
  const original = globalThis.fetch
  let down = true
  globalThis.fetch = (async () => { if (down) offline(); return new Response(JSON.stringify({ code: 'VALIDATION_FAILED', message: 'sai' }), { status: 422 }) }) as typeof fetch
  try {
    const e = await api.post('/partners', { code: 'Z' }).catch((x: unknown) => x) as InstanceType<typeof api.UncertainError>
    down = false
    await assert.rejects(api.retryPendingWrite(e.opId), (x: { status: number }) => x.status === 422)
    assert.equal(api.hasPendingWrite(), false)
  } finally { globalThis.fetch = original }
})

test('F04: thao tác chưa xác định được lưu cho tab và khôi phục sau tải lại (không có token), gắn với phiên hiện tại', async () => {
  const api = await import('./api.ts?f04-persist')
  const op = { id: 'k1', method: 'POST', path: '/vouchers', bodyText: '{"a":1}', key: 'k1', ctx: { token: 'T-old', companyId: 'C1' }, state: 'Uncertain' as const }
  const json = api.serializePending([op])
  assert.ok(!json.includes('T-old'), 'không lưu token')
  const restored = api.restorePending(json, 'T-new')
  assert.equal(restored.length, 1)
  assert.deepEqual([restored[0].key, restored[0].bodyText, restored[0].ctx], ['k1', '{"a":1}', { token: 'T-new', companyId: 'C1' }])
  assert.deepEqual(api.restorePending(json, null), [], 'chưa đăng nhập thì không khôi phục')
  assert.deepEqual(api.restorePending('không phải json', 'T'), [])
  assert.deepEqual(api.restorePending(JSON.stringify([{ id: 1 }]), 'T'), [], 'bản ghi hỏng bị bỏ')
})
