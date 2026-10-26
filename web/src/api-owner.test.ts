import test from 'node:test'
import assert from 'node:assert/strict'

let serial = 0
async function fixture(run: (api: any, storage: Map<string, string>) => Promise<void>) {
  const oldStorage = Object.getOwnPropertyDescriptor(globalThis, 'sessionStorage')
  const oldFetch = globalThis.fetch
  const storage = new Map<string, string>()
  Object.defineProperty(globalThis, 'sessionStorage', { configurable: true, value: {
    getItem: (k: string) => storage.get(k) ?? null,
    setItem: (k: string, v: string) => storage.set(k, v), removeItem: (k: string) => storage.delete(k),
  } })
  try {
    const api = await import(`./api.ts?owner-${++serial}`)
    api.session.setToken('fixture-token-A'); api.session.setCompany('company-A')
    await run(api, storage)
  } finally {
    globalThis.fetch = oldFetch
    if (oldStorage) Object.defineProperty(globalThis, 'sessionStorage', oldStorage)
    else Reflect.deleteProperty(globalThis, 'sessionStorage')
  }
}
const reload = () => import(`./api.ts?reload-${++serial}`)

test('late failure after logout cannot restore an old payload in another login, even after reload', () => fixture(async (api, storage) => {
  let calls = 0
  globalThis.fetch = async () => {
    calls++
    api.session.setToken(null)
    api.session.setToken('fixture-token-B')
    throw new TypeError('lost response')
  }
  await assert.rejects(api.post('/vouchers', { description: 'OLD-PRIVATE-PAYLOAD' }), api.isStaleContext)
  assert.equal(calls, 1)
  assert.equal(storage.has('hak.pending'), false)
  assert.ok(!JSON.stringify([...storage]).includes('OLD-PRIVATE-PAYLOAD'))
  const next = await reload()
  assert.equal(await next.retryPendingWrite(), false)
  assert.equal(next.pendingReviewNeeded(), true)
  next.acknowledgePendingReview()
  assert.equal(storage.has('hak.pending.review'), false)
}))

test('logout/login with the identical token rotates ownership and drops late success', () => fixture(async (api) => {
  const owner = api.session.ownerId
  globalThis.fetch = async () => {
    api.session.setToken(null); api.session.setToken('fixture-token-A')
    return new Response('{"id":"old-result"}')
  }
  await assert.rejects(api.post('/vouchers', {}), api.isStaleContext)
  assert.notEqual(api.session.ownerId, owner)
  assert.equal(api.hasPendingWrite(), false)
  assert.equal(api.pendingReviewNeeded(), true)
}))

test('a late old-owner callback cannot delete or overwrite a new-owner pending write', () => fixture(async (api, storage) => {
  let started!: () => void; let release!: () => void
  const ready = new Promise<void>(r => { started = r })
  const gate = new Promise<void>(r => { release = r })
  globalThis.fetch = async (_url, init) => {
    if (String(init!.body).includes('old-owner')) { started(); await gate }
    throw new TypeError('lost')
  }
  const old = api.post('/vouchers', { description: 'old-owner' }).catch((e: unknown) => e)
  await ready
  api.session.setToken(null); api.session.setToken('fixture-token-B')
  await assert.rejects(api.post('/vouchers', { description: 'new-owner' }))
  const before = storage.get('hak.pending')
  release()
  assert.ok(api.isStaleContext(await old))
  assert.equal(storage.get('hak.pending'), before)
  assert.equal(api.pendingWrites().length, 1)
  assert.ok(storage.get('hak.pending')!.includes('new-owner'))
  assert.ok(!storage.get('hak.pending')!.includes('old-owner'))
}))

test('a company switch stops automatic dispatch; reload retains the pending write only for its original company', () => fixture(async (api) => {
  const calls: RequestInit[] = []
  globalThis.fetch = async (_url, init) => {
    calls.push(init!); api.session.setCompany('company-B'); throw new TypeError('lost')
  }
  await assert.rejects(api.post('/vouchers', { description: 'company-A-only' }), api.isStaleContext)
  const next = await reload()
  assert.equal(await next.retryPendingWrite(), false)
  assert.equal(calls.length, 1)
  next.session.setCompany('company-A')
  globalThis.fetch = async (_url, init) => { calls.push(init!); return new Response('{}') }
  assert.equal(await next.retryPendingWrite(), true)
  assert.deepEqual(calls[0].headers, calls[1].headers)
  assert.equal(calls[0].body, calls[1].body)
}))

test('an old 401 cannot expire a new login with the same token', () => fixture(async (api) => {
  let expired = 0
  api.setSessionExpiredHandler(() => expired++)
  globalThis.fetch = async () => {
    api.session.setToken(null); api.session.setToken('fixture-token-A')
    return new Response('{"code":"UNAUTHENTICATED"}', { status: 401 })
  }
  await assert.rejects(api.get('/partners'))
  assert.equal(expired, 0)
  assert.equal(api.session.token, 'fixture-token-A')
}))

test('same-session reload preserves bytes, company and key; concurrent manual retries send once', () => fixture(async (api, storage) => {
  const calls: RequestInit[] = []
  globalThis.fetch = async (_url, init) => { calls.push(init!); throw new TypeError('lost') }
  await assert.rejects(api.post('/vouchers', { description: 'frozen' }), (e: any) => e.code === api.UNCERTAIN_CODE)
  assert.ok(!storage.get('hak.pending')!.includes('fixture-token-A'))
  const next = await reload()
  assert.equal(next.pendingWrites().length, 1)
  let release!: () => void
  const gate = new Promise<void>(r => { release = r })
  globalThis.fetch = async (_url, init) => { calls.push(init!); await gate; return new Response('{}') }
  const a = next.retryPendingWrite(); const b = next.retryPendingWrite()
  release(); await Promise.all([a, b])
  assert.equal(calls.length, 4)
  assert.equal(calls[3].body, calls[0].body)
  assert.deepEqual(calls[3].headers, calls[0].headers)
  assert.equal(next.hasPendingWrite(), false)
  assert.equal(storage.has('hak.pending'), false)
}))

test('changing only the stored token cannot bind pending writes to a new server idempotency scope', () => fixture(async (api, storage) => {
  globalThis.fetch = async () => { throw new TypeError('lost') }
  await assert.rejects(api.post('/vouchers', {}))
  storage.set('hak.token', 'fixture-token-B')
  const next = await reload()
  assert.equal(next.hasPendingWrite(), false)
  assert.equal(storage.has('hak.pending'), false)
  assert.equal(next.pendingReviewNeeded(), true)
}))

test('reload during the very first request restores its original key, before any network error', () => fixture(async (api, storage) => {
  let started!: () => void; let release!: () => void
  const ready = new Promise<void>(r => { started = r })
  const gate = new Promise<void>(r => { release = r })
  const calls: RequestInit[] = []
  globalThis.fetch = async (_url, init) => { calls.push(init!); started(); await gate; return new Response('{}') }
  const request = api.post('/vouchers', { description: 'reload-in-flight' })
  await ready
  assert.ok(storage.has('hak.pending'))
  assert.equal(api.hasPendingWrite(), false, 'ordinary saving does not show the uncertain retry button')
  const next = await reload()
  assert.equal(next.pendingWrites().length, 1)
  release(); await request
  globalThis.fetch = async (_url, init) => { calls.push(init!); return new Response('{}') }
  await next.retryPendingWrite()
  assert.equal(calls.length, 2)
  assert.deepEqual(calls[0].headers, calls[1].headers)
  assert.equal(calls[0].body, calls[1].body)
}))

test('sensitive routes are sent once without idempotency key or persistent passwords', () => fixture(async (api, storage) => {
  const paths = ['/auth/login', '/setup/admin', '/admin/users', '/backups/create', '/admin/backups', '/me/password', '/exchange/keys', '/x/../auth/login', '/%61uth/login', '/x%2f..%2fauth/login', '/einvoice/invoices/abc/issue', '/einvoice/documents/abc/retry', '/einvoice/process-pending', '/einvoice/notices/abc/send']
  let calls = 0
  globalThis.fetch = async (_url, init) => {
    calls++; assert.equal((init!.headers as Record<string, string>)['Idempotency-Key'], undefined)
    throw new TypeError('lost')
  }
  for (const path of paths) await assert.rejects(api.post(path, { password: 'FAKE-SECRET-DO-NOT-PERSIST' }))
  assert.equal(calls, paths.length)
  assert.equal(api.hasPendingWrite(), false)
  assert.ok(!JSON.stringify([...storage]).includes('FAKE-SECRET-DO-NOT-PERSIST'))
}))

test('legacy unbound storage is purged with a generic review notice', () => fixture(async (_api, storage) => {
  storage.set('hak.pending', JSON.stringify([{ path: '/auth/login', bodyText: 'OLD-SECRET' }]))
  const next = await reload()
  assert.equal(next.hasPendingWrite(), false)
  assert.equal(next.pendingReviewNeeded(), true)
  assert.ok(!JSON.stringify([...storage]).includes('OLD-SECRET'))
}))

test('expired records and sensitive entries inside a valid envelope are never restored', () => fixture(async (api) => {
  const op = { id: 'fixture-op', key: 'fixture-key', method: 'POST', path: '/vouchers', bodyText: '{}',
    ctx: { token: api.session.token, companyId: api.session.companyId, authOwner: api.session.ownerId }, createdAt: Date.now(), state: 'Uncertain' }
  const data = JSON.parse(api.serializePending([op]))
  for (const changed of [{ createdAt: Date.now() - 6 * 86400000 }, { createdAt: Date.now() + 600000 }, { path: '/admin/users' }, { key: 'bad' }]) {
    const json = JSON.stringify({ ...data, ops: [{ ...data.ops[0], ...changed }] })
    assert.deepEqual(api.restorePending(json, api.session.token), [])
  }
}))

test('lost response body, proxy error and malformed success all retry with the original key', () => fixture(async (api) => {
  const keys: string[] = []
  globalThis.fetch = async (_url, init) => {
    keys.push((init!.headers as Record<string, string>)['Idempotency-Key'])
    if (keys.length === 1) return { status: 200, text: async () => { throw new TypeError('body lost') } } as Response
    if (keys.length === 2) return new Response('proxy failed', { status: 503 })
    return new Response('malformed-success', { status: 200 })
  }
  await assert.rejects(api.post('/vouchers', {}), (e: any) => e.code === api.UNCERTAIN_CODE)
  assert.equal(keys.length, 3)
  assert.equal(new Set(keys).size, 1)
  assert.equal(api.hasPendingWrite(), true)
}))

test('raw signed bytes are sent once and a stale success is not delivered to another company', () => fixture(async (api) => {
  let calls = 0
  globalThis.fetch = async (_url, init) => {
    calls++; assert.equal(init!.body, ' {"signed":true}\n')
    assert.equal((init!.headers as Record<string, string>)['Idempotency-Key'], undefined)
    api.session.setCompany('company-B')
    return new Response('{}')
  }
  await assert.rejects(api.postRaw('/exchange/import', ' {"signed":true}\n'), api.isStaleContext)
  assert.equal(calls, 1)
  assert.equal(api.hasPendingWrite(), false)
}))
