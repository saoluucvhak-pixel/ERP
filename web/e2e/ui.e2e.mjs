// Test giao diện bằng trình duyệt thật (chạy qua `npm run e2e`): mỗi test gắn với một lỗi / yêu cầu đã sửa trong rà soát 24/10/2026.
import test, { after, afterEach, before } from 'node:test'
import assert from 'node:assert/strict'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { chromium } from 'playwright'

const API = process.env.E2E_API
const WEB = process.env.E2E_WEB
const creds = JSON.parse(readFileSync(new URL('../.dev-credentials.json', import.meta.url), 'utf8'))

const call = async (method, path, body, token, company) => {
  const res = await fetch(API + path, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(company ? { 'X-Company-Id': company } : {}) }, body: body ? JSON.stringify(body) : undefined })
  const text = await res.text()
  if (!res.ok) throw new Error(`${method} ${path} → ${res.status} ${text.slice(0, 300)}`)
  return text ? JSON.parse(text) : null
}

let browser, ctx
// Không ngủ cố định: mỗi bước chờ ĐÚNG phần tử / trạng thái mà bước sau cần (Playwright tự đợi tới hạn 30 giây rồi báo lỗi cụ thể).
const visible = (page, selector, opts) => page.locator(selector, opts).first().waitFor({ state: 'visible' })
before(async () => {
  // E2E_BROWSER_CHANNEL (ví dụ 'chrome', 'msedge') hoặc E2E_BROWSER_PATH để dùng trình duyệt có sẵn (Windows không cài Chromium của Playwright).
  browser = await chromium.launch({ channel: process.env.E2E_BROWSER_CHANNEL || undefined, executablePath: process.env.E2E_BROWSER_PATH || undefined })
  const login = async user => (await call('POST', '/auth/login', { userName: user, password: creds.users[user] })).token
  const preparerToken = await login('ketoan')
  const company = (await call('GET', '/me/companies', null, preparerToken))[0].id
  const accs = await call('GET', '/accounts', null, preparerToken, company)
  const partners = await call('GET', '/partners', null, preparerToken, company)
  const items = await call('GET', '/items', null, preparerToken, company)
  ctx = { login, preparerToken, company, A: code => accs.find(a => a.code === code).id, P: code => partners.find(p => p.code === code).id, item: code => items.find(i => i.code === code).id }
})
after(async () => { await browser?.close() })

// Test đỏ thì giữ ảnh chụp + log console của mọi trang đang mở (e2e/artifacts, đã .gitignore) để chẩn đoán, không che lỗi.
const opened = []
afterEach(async t => {
  const pages = opened.splice(0)
  if (t.passed !== false) { await Promise.all(pages.map(p => p.page.close().catch(() => {}))); return }
  const dir = new URL('./artifacts/', import.meta.url)
  mkdirSync(dir, { recursive: true })
  const stem = t.name.replace(/[^\p{L}\p{N}]+/gu, '-').slice(0, 60)
  for (const [i, p] of pages.entries()) {
    await p.page.screenshot({ path: fileURLToPath(new URL(`${stem}-${i}.png`, dir)), fullPage: true }).catch(() => {})
    writeFileSync(new URL(`${stem}-${i}.log`, dir), p.log.join('\n'))
  }
})

async function open(user) {
  const page = await browser.newPage()
  const log = []
  page.on('console', m => log.push(`[${m.type()}] ${m.text()}`))
  page.on('pageerror', e => log.push(`[pageerror] ${e.message}`))
  opened.push({ page, log })
  page.on('dialog', d => d.accept())
  await page.goto(WEB + '/')
  await page.fill('input[type=text], input:not([type])', user)
  await page.fill('input[type=password]', creds.users[user])
  await page.click('button:has-text("Đăng nhập")')
  await page.locator('button:has-text("DEV01"), .quick-add-btn').first().waitFor()
  const company = page.locator('button', { hasText: 'DEV01' })
  if (await company.count()) await company.first().click()
  await page.waitForSelector('.quick-add-btn')
  return page
}
const selected = page => page.locator('select').evaluateAll(els => els.map(e => e.selectedOptions[0]?.textContent ?? ''))

test('Retry ownership: server commits, response is lost, new login and reload do not replay the old write', async () => {
  const page = await open('ktt')
  const nextToken = await ctx.login('ketoan')
  let sends = 0
  await page.route('**/api/partners', async route => {
    if (route.request().method() !== 'POST') return route.continue()
    sends++
    const response = await route.fetch()
    assert.ok(response.ok(), 'first request must actually commit in the temporary database')
    await page.evaluate(async token => {
      const api = await import('/src/api.ts')
      api.session.setToken(null); api.session.setToken(token)
    }, nextToken)
    await route.abort('failed')
  })
  const code = 'E2E-OWNER-REPLAY'
  const result = await page.evaluate(async code => {
    const api = await import('/src/api.ts')
    try { await api.post('/partners', { code, name: 'Temporary ownership regression' }); return 'unexpected-success' }
    catch (e) { return e.code }
  }, code)
  assert.equal(result, 'NGU_CANH_DA_DOI')
  await page.reload()
  await page.waitForSelector('.quick-add-btn')
  await page.getByText('Có thao tác ở phiên trước cần đối chiếu.', { exact: false }).waitFor()
  const state = await page.evaluate(async () => {
    const api = await import('/src/api.ts')
    return { pending: api.pendingWrites().length, retried: await api.retryPendingWrite(), stored: sessionStorage.getItem('hak.pending') }
  })
  assert.deepEqual(state, { pending: 0, retried: false, stored: null })
  assert.equal(sends, 1)
  const partners = await call('GET', '/partners', null, nextToken, ctx.company)
  assert.equal(partners.filter(p => p.code === code).length, 1, 'exactly one committed partner, no hidden replay')
  await page.getByRole('button', { name: 'Đã đọc nhắc nhở', exact: true }).click()
  assert.equal(await page.evaluate(() => sessionStorage.getItem('hak.pending.review')), null)
  await page.close()
})

test('Safe retry button confirms the original server result without creating a second record', async () => {
  const page = await open('ktt')
  const code = 'E2E-OWNER-SAFE-RETRY'
  const keys = []
  const loseResponse = async route => {
    if (route.request().method() !== 'POST') return route.continue()
    keys.push(route.request().headers()['idempotency-key'])
    const response = await route.fetch()
    assert.ok(response.ok())
    await route.abort('failed')
  }
  await page.route('**/api/partners', loseResponse)
  const result = await page.evaluate(async code => {
    const api = await import('/src/api.ts')
    try { await api.post('/partners', { code, name: 'Temporary safe retry regression' }); return 'unexpected-success' }
    catch (e) { return e.code }
  }, code)
  assert.equal(result, 'KET_QUA_CHUA_XAC_DINH')
  assert.equal(keys.length, 3)
  assert.equal(new Set(keys).size, 1)
  await page.unroute('**/api/partners', loseResponse)
  const confirmation = page.waitForResponse(r => r.url().endsWith('/api/partners') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Thử lại an toàn', exact: true }).click()
  const response = await confirmation
  assert.ok(response.ok())
  assert.equal(response.request().headers()['idempotency-key'], keys[0])
  await page.getByText('Máy chủ đã xác nhận thao tác', { exact: false }).waitFor()
  const token = await page.evaluate(() => sessionStorage.getItem('hak.token'))
  const partners = await call('GET', '/partners', null, token, ctx.company)
  assert.equal(partners.filter(p => p.code === code).length, 1)
  assert.equal(await page.evaluate(() => sessionStorage.getItem('hak.pending')), null)
  await page.close()
})

test('CKTM: Tiến độ liên kết hóa đơn vào chứng từ đã ghi và không báo hồ sơ thuế hoàn tất', async () => {
  const date = new Date().toISOString().slice(0, 10)
  const chief = await ctx.login('ktt')
  const original = await call('POST', '/invoices', { kind: 'Sales', partnerId: ctx.P('KH01'), invoiceSeries: 'E2E', invoiceNo: 'LIFECYCLE27', invoiceDate: date, postingDate: date,
    description: 'E2E rebate lifecycle source', lines: [{ unitPrice: '1000000', quantity: '1', accountId: ctx.A('SALESREV'), vatRate: '10' }] }, ctx.preparerToken, ctx.company)
  await call('POST', `/invoices/${original.id}/post?rowVersion=${original.rowVersion}`, {}, chief, ctx.company)
  const created = await call('POST', '/discounts/agreements', { kind: 'Sales', partnerId: ctx.P('KH01'), code: 'CYCLE27UI', name: 'E2E cycle', fromDate: date, toDate: date, tiers: [{ threshold: '0', percent: '2' }] }, chief, ctx.company)
  const agreement = (await call('GET', '/discounts/agreements?kind=Sales', null, chief, ctx.company)).find(a => a.id === created.id)
  const settled = await call('POST', `/discounts/agreements/${agreement.id}/settle`, { rowVersion: agreement.rowVersion, byVoucher: true, invoiceSeries: '', firstInvoiceNo: '', invoiceDate: date, postingDate: date }, ctx.preparerToken, ctx.company)
  const vouchers = await call('GET', '/vouchers?text=CYCLE27UI', null, ctx.preparerToken, ctx.company)
  let voucher = vouchers.items.find(v => v.sourceSystem === 'HAK.TRADEDISC' && v.sourceId === settled.id)
  assert.ok(voucher, 'phải có chứng từ CKTM')
  voucher = await call('POST', `/vouchers/${voucher.id}/submit?rowVersion=${voucher.rowVersion}`, {}, ctx.preparerToken, ctx.company)
  voucher = await call('POST', `/vouchers/${voucher.id}/approve?rowVersion=${voucher.rowVersion}`, {}, chief, ctx.company)
  await call('POST', `/vouchers/${voucher.id}/post?rowVersion=${voucher.rowVersion}`, {}, chief, ctx.company)
  const adjustment = await call('POST', '/invoices/adjustments', { originalInvoiceId: original.id, purpose: 'Discount', discountKind: 'Trade', invoiceSeries: 'E2E', invoiceNo: 'LIFECYCLE27-ADJ',
    invoiceDate: date, postingDate: date, description: 'E2E rebate legal invoice', lines: [{ originalLineNo: 1, amount: '20000' }] }, ctx.preparerToken, ctx.company)
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Bán hàng")')
  await page.getByRole('tab', { name: 'CK thương mại cho khách', exact: true }).click()
  const row = page.locator('tbody tr', { hasText: 'CYCLE27UI' })
  await row.getByRole('button', { name: 'Tiến độ / Hóa đơn', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Tiến độ quyết toán — CYCLE27UI', exact: true })
  await dialog.waitFor()
  assert.match(await dialog.textContent(), /Đã ghi sổ/)
  assert.match(await dialog.textContent(), /Chưa đủ hóa đơn/)
  await dialog.locator('select').selectOption(adjustment.id)
  const linkResponse = page.waitForResponse(r => r.url().endsWith(`/discounts/agreements/${agreement.id}/link-invoice`) && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: 'Liên kết hóa đơn', exact: true }).click()
  assert.ok((await linkResponse).ok())
  await dialog.locator('tbody tr', { hasText: adjustment.number }).waitFor()
  await call('POST', `/invoices/${adjustment.id}/post?rowVersion=${adjustment.rowVersion}`, {}, chief, ctx.company)
  const progress = await call('GET', `/discounts/agreements/${agreement.id}/lifecycle`, null, chief, ctx.company)
  assert.equal(progress.postedInvoiceAmount, '20000')
  assert.equal(progress.legalInvoiceStatus, 'Missing', 'ghi sổ tay không tự xác nhận chứng cứ HĐĐT')
  assert.equal(progress.taxStatus, 'PendingInvoice')
  await page.close()
})

test('CKTM supplementary UI creates only the difference and keeps the original run visible', async () => {
  const date = new Date().toISOString().slice(0, 10)
  const chief = await ctx.login('ktt')
  const partner = await call('POST', '/partners', { code: 'AMEND28UI', name: 'Supplement UI fixture' }, chief, ctx.company)
  const sale = async (no, amount) => {
    const invoice = await call('POST', '/invoices', { kind: 'Sales', partnerId: partner.id, invoiceSeries: 'E2E', invoiceNo: no, invoiceDate: date, postingDate: date, description: 'Amendment UI source', lines: [{ unitPrice: amount, quantity: '1', accountId: ctx.A('SALESREV'), vatRate: '10' }] }, ctx.preparerToken, ctx.company)
    return call('POST', `/invoices/${invoice.id}/post?rowVersion=${invoice.rowVersion}`, {}, chief, ctx.company)
  }
  await sale('AMEND28-A', '1000000')
  const agreement = await call('POST', '/discounts/agreements', { kind: 'Sales', partnerId: partner.id, code: 'AMEND28UI', name: 'Supplement UI fixture', fromDate: date, toDate: date, tiers: [{ threshold: '0', percent: '2' }] }, chief, ctx.company)
  const a = (await call('GET', '/discounts/agreements?kind=Sales', null, chief, ctx.company)).find(a => a.id === agreement.id)
  const root = await call('POST', `/discounts/agreements/${a.id}/settle`, { rowVersion: a.rowVersion, invoiceSeries: 'E2E', firstInvoiceNo: 'AMEND28-ROOT', invoiceDate: date, postingDate: date }, ctx.preparerToken, ctx.company)
  const rootProgress = await call('GET', `/discounts/agreements/${a.id}/lifecycle`, null, chief, ctx.company)
  for (const row of rootProgress.invoices) {
    const invoice = await call('GET', `/invoices/${row.id}`, null, chief, ctx.company)
    await call('POST', `/invoices/${invoice.id}/post?rowVersion=${invoice.rowVersion}`, {}, chief, ctx.company)
  }
  await sale('AMEND28-B', '200000')
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Bán hàng")')
  await page.getByRole('tab', { name: 'CK thương mại cho khách', exact: true }).click()
  await page.locator('tbody tr', { hasText: 'AMEND28UI' }).getByRole('button', { name: 'Bổ sung / Thu hồi', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Bổ sung / thu hồi CKTM — AMEND28UI', exact: true })
  await dialog.getByRole('button', { name: 'Tính lại chênh lệch', exact: true }).click()
  await dialog.getByLabel('Lý do', { exact: true }).fill('Late invoice evidence UI28')
  await dialog.getByLabel('Ký hiệu', { exact: true }).fill('E2E')
  await dialog.getByLabel('Số hóa đơn đầu tiên', { exact: true }).fill('AMEND28-SUP')
  const submitted = page.waitForResponse(r => r.url().endsWith(`/discounts/agreements/${a.id}/amend`) && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: 'Lập hóa đơn nháp theo chênh lệch', exact: true }).click()
  const response = await submitted; assert.ok(response.ok())
  const child = await response.json(); assert.equal(child.amount, '4000')
  const history = await call('GET', `/discounts/agreements/${a.id}/history`, null, chief, ctx.company)
  assert.equal(history.length, 2); assert.ok(history.some(h => h.id === root.id && h.amount === '20000'))
  const row = dialog.locator('tbody tr', { hasText: 'Late invoice evidence UI28' })
  await row.getByRole('button', { name: 'Tiến độ lần này', exact: true }).click()
  const progress = page.getByRole('dialog', { name: 'Tiến độ quyết toán — AMEND28UI', exact: true })
  await progress.waitFor(); assert.match(await progress.textContent(), /Nháp/)
  const state = await call('GET', `/discounts/agreements/${a.id}/lifecycle?runId=${child.id}`, null, chief, ctx.company)
  assert.equal(state.accountingStatus, 'Draft'); assert.equal(state.amount, '4000')
  await page.close()
})

test('R05: Thêm nhanh mở đúng loại phiếu, kể cả khi đang ở đúng màn', async () => {
  const page = await open('ktt')
  const quick = async (label, want, msg) => {
    await page.click('.quick-add-btn'); await page.click(`.quick-add-pop >> text="${label}"`)
    await page.waitForFunction(w => [...document.querySelectorAll('select')].some(e => e.selectedOptions[0]?.textContent === w), want, { timeout: 15000 }).catch(() => {})
    assert.ok((await selected(page)).includes(want), msg)
  }
  await quick('Chi tiền', 'Phiếu chi', 'Chi tiền phải mở Phiếu chi')
  await quick('Thu tiền', 'Phiếu thu', 'Thu tiền (cùng màn) phải mở Phiếu thu')
  await quick('Chi tiền', 'Phiếu chi', 'Chi tiền lần hai phải mở lại Phiếu chi')
  await quick('Nhập kho', 'Phiếu nhập', 'Nhập kho')
  await quick('Xuất kho', 'Phiếu xuất', 'Xuất kho')
  await page.click('.quick-add-btn'); await page.click('.quick-add-pop >> text="Kiểm kê"'); await visible(page, 'button.primary:has-text("Kiểm kê")')
  assert.match(await page.locator('button.primary').first().textContent(), /Kiểm kê/)
  await page.close()
})

test('R04: Kiểm tra chứng từ Nợ 100 / Có 90 hiện lỗi chi tiết, không bị thông báo chung ghi đè', async () => {
  const today = new Date(); const d = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  const v = await call('POST', '/vouchers', { voucherType: 'GJ', voucherDate: d, postingDate: d, description: 'E2E lệch Nợ/Có',
    lines: [{ accountId: ctx.A('EXP'), side: 'D', amount: '100' }, { accountId: ctx.A('AP'), side: 'C', amount: '90', partnerId: ctx.P('NCC01') }] }, ctx.preparerToken, ctx.company)
  const errs = await call('POST', `/vouchers/${v.id}/validate`, null, ctx.preparerToken, ctx.company)
  assert.ok(errs.length > 0, 'API phải báo lỗi cân đối')
  const page = await open('ketoan')
  await page.click('.sidebar button:has-text("Tổng hợp")')
  await page.click('[role=tab]:has-text("Chứng từ kế toán")')
  await page.locator('tr', { hasText: v.number }).first().click()
  await page.click('button:has-text("Kiểm tra")')
  const msg = page.locator('.msg.error')
  await msg.first().waitFor({ state: 'visible' })
  assert.ok(await msg.count() > 0, 'lỗi kiểm tra phải hiện màu lỗi')
  const text = await msg.first().textContent()
  assert.ok(text.includes(errs[0].message), `thông báo phải chứa lỗi chi tiết "${errs[0].message}", thực tế: ${text}`)
  assert.ok(!text.includes('xem kết quả bên dưới'), 'không được bị thông báo chung ghi đè')
  await page.close()
})

test('Worklist: người lập thấy việc duyệt bị chặn, người duyệt mở thẳng đúng chứng từ', async () => {
  const v = await call('POST', '/vouchers', { voucherType: 'GJ', voucherDate: '2026-01-05', postingDate: '2026-01-05', description: 'E2E chờ duyệt',
    lines: [{ accountId: ctx.A('EXP'), side: 'D', amount: '1000' }, { accountId: ctx.A('AP'), side: 'C', amount: '1000', partnerId: ctx.P('NCC01') }] }, ctx.preparerToken, ctx.company)
  await call('POST', `/vouchers/${v.id}/submit?rowVersion=${v.rowVersion}`, {}, ctx.preparerToken, ctx.company)
  const worklistRow = page => page.locator('section[aria-label="Việc cần làm theo chứng từ"] tbody tr', { hasText: v.number })

  const preparer = await open('ketoan')
  await preparer.click('.sidebar button:has-text("Quản lý chu trình")'); await worklistRow(preparer).waitFor()
  assert.match(await worklistRow(preparer).textContent(), /Cần quyền duyệt/)
  await preparer.close()

  const approver = await open('ktt')
  await approver.click('.sidebar button:has-text("Quản lý chu trình")')
  const row = worklistRow(approver); await row.waitFor()
  assert.match(await row.textContent(), /Làm được ngay/)
  await row.locator('button:has-text("Mở")').click(); await visible(approver, '.panel strong', { hasText: v.number })
  assert.ok(await approver.locator('.panel strong', { hasText: v.number }).count() > 0, 'chi tiết phải mở đúng chứng từ ' + v.number)
  assert.ok(await approver.locator('button:has-text("Duyệt")').count() > 0, 'phải có nút Duyệt')
  await approver.close()
})

test('Chu trình mua / bán không lẫn: mỗi phân hệ chỉ có đơn của chiều mình và không còn nút chuyển', async () => {
  for (const [kind, partner, desc] of [['Sales', 'KH01', 'E2E đơn BÁN'], ['Purchase', 'NCC01', 'E2E đơn MUA']])
    await call('POST', '/orders', { kind, partnerId: ctx.P(partner), orderDate: '2026-10-20', description: desc, contractNo: null, contractId: null, overDeliveryTolerancePercent: '0', currency: null, expectedDate: null,
      lines: [{ itemId: ctx.item('GOKEO'), quantity: '5', unitPrice: '1000', basis: 'Quantity', vatRate: null, description: null }] }, ctx.preparerToken, ctx.company)
  const page = await open('ktt')
  for (const [module, tab, mine, other] of [['Mua hàng', 'Đơn mua hàng', 'E2E đơn MUA', 'E2E đơn BÁN'], ['Bán hàng', 'Đơn bán hàng', 'E2E đơn BÁN', 'E2E đơn MUA']]) {
    await page.click(`.sidebar button:has-text("${module}")`)
    await page.click(`[role=tab]:has-text("${tab}")`)
    await visible(page, 'main', { hasText: mine })
    const body = await page.locator('main').textContent()
    assert.ok(body.includes(mine), `${module}: phải thấy ${mine}`)
    assert.ok(!body.includes(other), `${module}: không được thấy ${other}`)
    assert.equal(await page.locator('.toolbar button:has-text("Đơn bán"), .toolbar button:has-text("Đơn mua")').count(), 0, `${module}: không còn nút chuyển bán/mua`)
  }
  await page.close()
})

test('Worklist: đề nghị mua sắm tài sản chờ duyệt mở thẳng và tô nổi đúng dòng', async () => {
  const req = await call('POST', '/assets/requests', { class: 'FixedAsset', name: 'E2E máy xúc', department: 'Sản xuất', estimatedCost: '50000000', neededDate: '2026-12-31', reason: 'E2E' }, ctx.preparerToken, ctx.company)
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Quản lý chu trình")')
  const row = page.locator('section[aria-label="Việc cần làm theo chứng từ"] tbody tr', { hasText: req.number }); await row.waitFor()
  assert.match(await row.textContent(), /Duyệt đề nghị mua sắm tài sản/)
  assert.match(await row.textContent(), /Làm được ngay/)
  await row.locator('button:has-text("Mở")').click()
  const focused = page.locator('tr.focus-row'); await focused.first().waitFor()
  assert.equal(await focused.count(), 1)
  assert.ok((await focused.textContent()).includes(req.number))
  await page.close()
})

test('Cảnh báo nội dung nhập dở: đổi phân hệ khi đang nhập phiếu thì hỏi, hủy thì ở lại', async () => {
  const page = await open('ktt')
  page.removeAllListeners('dialog')
  const asked = []
  page.on('dialog', d => { asked.push(d.message()); d.dismiss() })
  await page.click('.quick-add-btn'); await page.click('.quick-add-pop >> text="Thu tiền"')
  await page.fill('label:has-text("Diễn giải") input', 'E2E đang nhập dở')
  await page.click('.sidebar button:has-text("Kho")')
  assert.equal(asked.length, 1, 'phải hỏi trước khi rời')
  assert.match(asked[0], /chưa lưu/)
  assert.equal(await page.locator('label:has-text("Diễn giải") input').inputValue(), 'E2E đang nhập dở', 'hủy thì ở lại, giữ nội dung')
  page.removeAllListeners('dialog'); page.on('dialog', d => d.accept())
  await page.click('.sidebar button:has-text("Kho")'); await page.locator('label:has-text("Diễn giải") input').waitFor({ state: 'detached' })
  assert.equal(await page.locator('label:has-text("Diễn giải") input').count(), 0, 'đồng ý thì rời màn')
  await page.close()
})

test('data.export: người chỉ có quyền xem (kiểm toán nội bộ) không thấy nút Xuất Excel, kế toán trưởng thấy', async () => {
  const admin = (await call('POST', '/auth/login', { userName: 'admin', password: creds.users.admin })).token
  const pw = 'E2e-Password-123'
  const u = await call('POST', '/admin/users', { userName: 'e2eaudit', fullName: 'Kiểm toán E2E', password: pw }, admin)
  await call('POST', '/admin/grants', { userId: u.id, companyId: ctx.company, roleCode: 'INTERNAL_AUDITOR' }, admin)
  const openAs = async (user, password) => {
    const page = await browser.newPage(); page.on('dialog', d => d.accept())
    await page.goto(WEB + '/')
    await page.fill('input[type=text], input:not([type])', user); await page.fill('input[type=password]', password)
    await page.click('button:has-text("Đăng nhập")')
    await page.locator('button:has-text("DEV01"), .quick-add-btn').first().waitFor()
    const company = page.locator('button', { hasText: 'DEV01' }); if (await company.count()) await company.first().click()
    await page.waitForSelector('.quick-add-btn')
    await page.click('.sidebar button:has-text("Tổng hợp")')
    await page.click('[role=tab]:has-text("Chứng từ kế toán")')
    await visible(page, 'button:has-text("In")')   // thanh công cụ đã dựng, quyền đã nạp
    return page
  }
  const auditor = await openAs('e2eaudit', pw)
  await auditor.waitForFunction(() => fetch('/api/me/permissions', { headers: { Authorization: 'Bearer ' + sessionStorage.getItem('hak.token'), 'X-Company-Id': sessionStorage.getItem('hak.company') } }).then(r => r.ok))
  assert.equal(await auditor.locator('button:has-text("Xuất Excel")').count(), 0, 'kiểm toán không có data.export')
  assert.ok(await auditor.locator('button:has-text("In")').count() > 0, 'vẫn in được')
  await auditor.close()
  const chief = await openAs('ktt', creds.users.ktt)
  await visible(chief, 'button:has-text("Xuất Excel")')
  assert.ok(await chief.locator('button:has-text("Xuất Excel")').count() > 0, 'kế toán trưởng có data.export')
  await chief.close()
})

test('Phân trang phía máy chủ: phiếu kho trả tổng số, cờ còn thêm và trang kế tiếp', async () => {
  const wh = (await call('GET', '/warehouses', null, ctx.preparerToken, ctx.company))[0].id
  for (const day of ['2026-02-01', '2026-02-02', '2026-02-03'])
    await call('POST', '/stock-documents', { type: 'Receipt', warehouseId: wh, documentDate: day, postingDate: day, description: 'E2E phân trang',
      lines: [{ itemId: ctx.item('DAM'), quantity: '1', amount: '1000', counterAccountId: ctx.A('GRNI') }] }, ctx.preparerToken, ctx.company)
  const q = '/stock-documents/page?from=2026-02-01&to=2026-02-28'
  const first = await call('GET', `${q}&skip=0&take=2`, null, ctx.preparerToken, ctx.company)
  assert.equal(first.items.length, 2); assert.equal(first.total, 3); assert.equal(first.hasMore, true)
  assert.ok(first.items[0].postingDate >= first.items[1].postingDate, 'mới nhất trước')
  const next = await call('GET', `${q}&skip=2&take=2`, null, ctx.preparerToken, ctx.company)
  assert.equal(next.items.length, 1); assert.equal(next.hasMore, false)
  assert.ok(!first.items.some(x => x.id === next.items[0].id), 'trang sau không trùng trang trước')
})

test('Công nợ và Chiết khấu thanh toán tách theo chiều: Bán hàng chỉ phải thu, Mua hàng chỉ phải trả, không còn nút chuyển', async () => {
  const page = await open('ktt')
  const accountOptions = async () => page.locator('label:has-text("Tài khoản công nợ") select option').evaluateAll(os => os.map(o => o.textContent.trim()))
  for (const [module, tabName, want, notWant, discount] of [['Mua hàng', 'Công nợ phải trả', 'AP', 'AR', 'CK thanh toán được hưởng'], ['Bán hàng', 'Công nợ phải thu', 'AR', 'AP', 'CK thanh toán cho khách']]) {
    await page.click(`.sidebar button:has-text("${module}")`)
    await page.click(`[role=tab]:has-text("${tabName}")`)
    await page.locator('label:has-text("Tài khoản công nợ") select option').nth(1).waitFor({ state: 'attached' })   // danh mục TK đã nạp
    const opts = (await accountOptions()).join('|')
    assert.ok(opts.includes(want + ' ') || opts.includes(want + '–') || opts.includes(want), `${module}: phải có TK ${want}`)
    assert.ok(!new RegExp(`(^|\\|)${notWant}\\b`).test(opts), `${module}: không được có TK ${notWant}, thực tế: ${opts}`)
    await page.click(`[role=tab]:has-text("${discount}")`); await visible(page, `[role=tab][aria-selected=true]:has-text("${discount}")`)
    assert.equal(await page.locator('.toolbar button:has-text("Hóa đơn mua"), .toolbar button:has-text("Hóa đơn bán")').count(), 0, `${module}: chiết khấu không còn nút chuyển`)
  }
  await page.close()
})

test('Worklist: lệnh sản xuất nháp chờ ban hành mở thẳng và chọn sẵn đúng lệnh', async () => {
  const damsx = ctx.item('DAMSX'), gokeo = ctx.item('GOKEO')
  await call('PUT', `/production/boms/${damsx}`, { lines: [{ rawItemId: gokeo, quantityPerUnit: '1', scrapRate: '0' }], note: 'E2E định mức' }, ctx.preparerToken, ctx.company)
  const objects = await call('GET', '/production/cost-objects', null, ctx.preparerToken, ctx.company)
  const wh = (await call('GET', '/warehouses', null, ctx.preparerToken, ctx.company))[0].id
  const order = await call('POST', '/production/orders', { itemId: damsx, costObjectId: objects.find(o => o.code === 'SX-DAM').id, warehouseId: wh, plannedQuantity: '10', plannedStart: '2026-11-01', note: 'E2E lệnh sản xuất' }, ctx.preparerToken, ctx.company)
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Quản lý chu trình")')
  const row = page.locator('section[aria-label="Việc cần làm theo chứng từ"] tbody tr', { hasText: order.number }); await row.waitFor()
  assert.match(await row.textContent(), /Ban hành lệnh sản xuất/)
  assert.match(await row.textContent(), /Làm được ngay/)
  await row.locator('button:has-text("Mở")').click()
  const selectedRow = page.locator('tr.selected'); await selectedRow.first().waitFor()
  assert.equal(await selectedRow.count(), 1)
  assert.ok((await selectedRow.textContent()).includes(order.number))
  await page.close()
})

test('Chiết khấu thương mại: thỏa thuận theo doanh số lập ở Bán hàng, không lẫn sang Mua hàng; hóa đơn có cột chiết khấu', async () => {
  const visible = (pg, selector, opts) => pg.locator(selector, opts).first().waitFor({ state: 'visible' })
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Bán hàng")')
  await page.click('[role=tab]:has-text("CK thương mại cho khách")')
  await visible(page, 'h2:has-text("Chiết khấu thương mại cho khách")')
  await page.click('button:has-text("Lập thỏa thuận")')
  const partner = page.locator('.modal label:has-text("Khách hàng") select').first()
  await partner.selectOption(await partner.locator('option', { hasText: 'KH01' }).first().getAttribute('value'))
  await page.fill('.modal label:has-text("Mã") input', 'E2E-CK')
  await page.fill('.modal label:has-text("Tên") input', 'Chiết khấu E2E')
  await page.fill('[aria-label="Doanh số từ"]', '1000000')
  await page.fill('[aria-label="Chiết khấu %"]', '2')
  await page.click('button.primary:has-text("Lưu")')
  await visible(page, 'tr', { hasText: 'E2E-CK' })
  // Bên mua không thấy thỏa thuận của chiều bán.
  await page.click('.sidebar button:has-text("Mua hàng")')
  await page.click('[role=tab]:has-text("CK thương mại được hưởng")')
  await visible(page, 'h2:has-text("Chiết khấu thương mại được hưởng")')
  await visible(page, 'td.muted:has-text("Chưa có thỏa thuận")')
  assert.equal(await page.locator('tr', { hasText: 'E2E-CK' }).count(), 0, 'thỏa thuận chiều bán không được hiện ở Mua hàng')
  // Hóa đơn có cột chiết khấu thương mại trên từng dòng.
  await page.click('[role=tab]:has-text("Hóa đơn mua")')
  await visible(page, 'th:has-text("CK %")')
  await page.close()
})

test('Hóa đơn điện tử: màn hình báo nhà cung cấp giả lập; form hóa đơn bán cho lấy số từ nhà cung cấp', async () => {
  const visible = (pg, selector, opts) => pg.locator(selector, opts).first().waitFor({ state: 'visible' })
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Bán hàng")')
  await page.click('[role=tab]:has-text("Hóa đơn điện tử")')
  await visible(page, 'h2:has-text("Hóa đơn điện tử")')
  await visible(page, '.msg:has-text("GIẢ LẬP")')
  await visible(page, 'td.muted:has-text("Chưa có hóa đơn phát hành từ HAK")')
  await page.click('.toolbar button:has-text("Thông báo sai sót")')
  await visible(page, 'td.muted:has-text("Chưa có thông báo sai sót")')
  await page.click('[role=tab]:has-text("Hóa đơn bán")')
  const box = page.locator('label:has-text("Lấy số từ nhà cung cấp HĐĐT") input[type=checkbox]')
  await box.waitFor({ state: 'visible' })
  await box.check()
  assert.equal(await page.locator('label:has-text("Số HĐ") input').first().isDisabled(), true, 'số hóa đơn do nhà cung cấp cấp, không gõ tay')
  await page.click('.sidebar button:has-text("Mua hàng")')
  await page.click('[role=tab]:has-text("HĐĐT nhà cung cấp")')
  await visible(page, 'h2:has-text("Hóa đơn điện tử của nhà cung cấp")')
  await visible(page, 'td.muted:has-text("Không có điều chỉnh / thay thế nào của nhà cung cấp")')
  await page.close()
})

test('Foreign replacement UI previews transferred foreign and book amounts before issue', async () => {
  const date = new Date().toISOString().slice(0, 10)
  const chief = await ctx.login('ktt')
  const original = await call('POST', '/invoices', { kind: 'Sales', partnerId: ctx.P('KH01'), invoiceSeries: '', invoiceNo: '', awaitIssue: true, currency: 'USD', exchangeRate: '25000', invoiceDate: date, postingDate: date, description: 'FX replacement UI source', lines: [{ unitPrice: '1000', quantity: '1', accountId: ctx.A('SALESREV'), vatRate: '10' }] }, ctx.preparerToken, ctx.company)
  const issued = await call('POST', `/einvoice/invoices/${original.id}/issue?rowVersion=${original.rowVersion}`, {}, chief, ctx.company)
  assert.equal(issued.postError, null)
  const items = await call('GET', `/settlement/open-items?accountId=${ctx.A('AR')}&partnerId=${ctx.P('KH01')}&asOf=${date}`, null, chief, ctx.company)
  const posted = await call('GET', `/invoices/${original.id}`, null, chief, ctx.company)
  const target = items.find(i => i.foreignCurrency === 'USD' && i.voucherId === posted.voucherId)
  assert.ok(target)
  const draft = await call('POST', '/fx/payments', { date, cashAccountId: ctx.A('BANKUSD'), paymentRate: '25200', description: 'Receipt before replacement UI', items: [{ journalLineId: target.journalLineId, foreignAmount: '400' }] }, ctx.preparerToken, ctx.company)
  let voucher = await call('GET', `/vouchers/${draft.id}`, null, ctx.preparerToken, ctx.company)
  voucher = await call('POST', `/vouchers/${voucher.id}/submit?rowVersion=${voucher.rowVersion}`, {}, ctx.preparerToken, ctx.company)
  voucher = await call('POST', `/vouchers/${voucher.id}/approve?rowVersion=${voucher.rowVersion}`, {}, chief, ctx.company)
  await call('POST', `/vouchers/${voucher.id}/post?rowVersion=${voucher.rowVersion}`, {}, chief, ctx.company)
  const replacement = await call('POST', `/einvoice/invoices/${original.id}/replacement`, { reason: 'Price correction UI', changes: [{ lineNo: 1, unitPrice: '900' }] }, ctx.preparerToken, ctx.company)
  const page = await open('ktt')
  await page.click('.sidebar button:has-text("Bán hàng")')
  await page.click('[role=tab]:has-text("Hóa đơn bán")')
  await page.locator('tbody tr', { hasText: replacement.number }).getByRole('button', { name: 'Phân bổ thay thế', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: `Phân bổ hóa đơn thay thế — ${replacement.number}`, exact: true })
  await dialog.waitFor()
  const cells = await dialog.locator('tbody tr').first().locator('td').allTextContents()
  assert.equal(cells[0], 'USD'); assert.equal(cells[1].replace(/\D/g, ''), '400')
  assert.equal(cells[2].replace(/\D/g, ''), '10000000')
  assert.equal((await call('GET', `/invoices/${replacement.id}`, null, chief, ctx.company)).status, 'Draft')
  await page.close()
})
