import test from 'node:test';
import assert from 'node:assert/strict';

test('automatic retry must not send an old form under a new company', async () => {
  const api = await import('./api.ts?automatic-context');
  api.session.setToken('test-session-A'); api.session.setCompany('company-A');
  const calls = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, init) => {
    calls.push({company:init.headers['X-Company-Id'], key:init.headers['Idempotency-Key']});
    if (calls.length === 1) {
      api.session.setCompany('company-B');
      throw new TypeError('Simulated response loss followed by company switch');
    }
    return new Response(JSON.stringify({id:'test-created'}), {status:200});
  };
  try {
    await assert.rejects(api.post('/partners', {code:'AUDIT-RETRY',name:'Only for company A'}), api.isStaleContext);
    assert.deepEqual(calls.map(c=>c.company), ['company-A']);
    assert.equal(api.hasPendingWrite(), false);
    api.session.setCompany('company-A');
    assert.equal(await api.retryPendingWrite(), true);
    assert.deepEqual(calls.map(c=>c.company), ['company-A','company-A']);
    assert.equal(calls[0].key, calls[1].key);
  } finally { globalThis.fetch = original; }
});

test('pending safe retry must not survive a different login/company silently', async () => {
  const api = await import('./api.ts?pending-context');
  api.session.setToken('test-session-A'); api.session.setCompany('company-A');
  const original = globalThis.fetch;
  globalThis.fetch = async () => { throw new TypeError('Simulated response loss'); };
  try {
    await assert.rejects(api.post('/partners',{code:'PENDING-A',name:'Company A only'}),e=>e.code===api.UNCERTAIN_CODE);
    api.session.setToken(null); api.session.setCompany(null);
    api.session.setToken('test-session-B'); api.session.setCompany('company-B');
    assert.equal(api.hasPendingWrite(), false, 'Old pending write remains available in the new session');
  } finally { globalThis.fetch = original; }
});

test('a GET answered after the company changed is dropped, not delivered to the new context', async () => {
  const api = await import('./api.ts?stale-get')
  api.session.setToken('test-session-A'); api.session.setCompany('company-A')
  const original = globalThis.fetch
  globalThis.fetch = async () => {
    api.session.setCompany('company-B')   // đổi công ty khi yêu cầu đang bay
    return new Response(JSON.stringify([{ id: 'data-of-company-A' }]), { status: 200 })
  }
  try {
    await assert.rejects(api.get('/partners'), e => api.isStaleContext(e))
    globalThis.fetch = async () => new Response(JSON.stringify([{ id: 'data-of-company-B' }]), { status: 200 })
    assert.deepEqual(await api.get('/partners'), [{ id: 'data-of-company-B' }])   // cùng ngữ cảnh: giao bình thường
  } finally { globalThis.fetch = original }
})

test('an identical write already in flight is shared (double click sends one request with one key); different writes are not', async () => {
  const api = await import('./api.ts?double-click')
  api.session.setToken('test-session-A'); api.session.setCompany('company-A')
  const original = globalThis.fetch
  const calls: { key: string; body: string }[] = []
  let release: () => void = () => {}
  const gate = new Promise<void>(r => { release = r })
  globalThis.fetch = async (_url, init) => {
    calls.push({ key: (init as { headers: Record<string, string> }).headers['Idempotency-Key'], body: String((init as { body?: string }).body) })
    await gate
    return new Response(JSON.stringify({ id: 'x' }), { status: 200 })
  }
  try {
    const a = api.post('/partners', { code: 'DUP', name: 'Một' })
    const b = api.post('/partners', { code: 'DUP', name: 'Một' })      // nhấp đúp: cùng nội dung
    const c = api.post('/partners', { code: 'OTHER', name: 'Hai' })    // thao tác khác: gửi riêng
    release()
    await Promise.all([a, b, c])
    assert.equal(calls.length, 2)
    assert.equal(calls.filter(x => x.body.includes('DUP')).length, 1)
    assert.notEqual(calls[0].key, calls[1].key)
    await api.post('/partners', { code: 'DUP', name: 'Một' })           // đã xong: thao tác y hệt sau đó là thao tác mới
    assert.equal(calls.length, 3)
  } finally { globalThis.fetch = original }
})
