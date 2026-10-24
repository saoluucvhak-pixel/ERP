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
    await api.post('/partners', {code:'AUDIT-RETRY',name:'Only for company A'});
    assert.deepEqual(calls.map(c=>c.company), ['company-A','company-A']);
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
