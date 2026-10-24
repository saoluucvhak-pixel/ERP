// Test giao diện bằng trình duyệt thật (chạy qua `npm run e2e`): mỗi test gắn với một lỗi / yêu cầu đã sửa trong rà soát 24/10/2026.
import test, { after, before } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
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
before(async () => {
  browser = await chromium.launch()
  const login = async user => (await call('POST', '/auth/login', { userName: user, password: creds.users[user] })).token
  const preparerToken = await login('ketoan')
  const company = (await call('GET', '/me/companies', null, preparerToken))[0].id
  const accs = await call('GET', '/accounts', null, preparerToken, company)
  const partners = await call('GET', '/partners', null, preparerToken, company)
  const items = await call('GET', '/items', null, preparerToken, company)
  ctx = { login, preparerToken, company, A: code => accs.find(a => a.code === code).id, P: code => partners.find(p => p.code === code).id, item: code => items.find(i => i.code === code).id }
})
after(async () => { await browser?.close() })

async function open(user) {
  const page = await browser.newPage()
  page.on('dialog', d => d.accept())
  await page.goto(WEB + '/')
  await page.fill('input[type=text], input:not([type])', user)
  await page.fill('input[type=password]', creds.users[user])
  await page.click('button:has-text("Đăng nhập")')
  await page.waitForTimeout(1200)
  const company = page.locator('button', { hasText: 'DEV01' })
  if (await company.count()) await company.first().click()
  await page.waitForSelector('.quick-add-btn')
  return page
}
const selected = page => page.locator('select').evaluateAll(els => els.map(e => e.selectedOptions[0]?.textContent ?? ''))

test('R05: Thêm nhanh mở đúng loại phiếu, kể cả khi đang ở đúng màn', async () => {
  const page = await open('ktt')
  const quick = async label => { await page.click('.quick-add-btn'); await page.click(`.quick-add-pop >> text="${label}"`); await page.waitForTimeout(700) }
  await quick('Chi tiền'); assert.ok((await selected(page)).includes('Phiếu chi'), 'Chi tiền phải mở Phiếu chi')
  await quick('Thu tiền'); assert.ok((await selected(page)).includes('Phiếu thu'), 'Thu tiền (cùng màn) phải mở Phiếu thu')
  await quick('Chi tiền'); assert.ok((await selected(page)).includes('Phiếu chi'), 'Chi tiền lần hai phải mở lại Phiếu chi')
  await quick('Nhập kho'); assert.ok((await selected(page)).includes('Phiếu nhập'), 'Nhập kho')
  await quick('Xuất kho'); assert.ok((await selected(page)).includes('Phiếu xuất'), 'Xuất kho')
  await quick('Kiểm kê'); assert.match(await page.locator('button.primary').first().textContent(), /Kiểm kê/)
  await page.close()
})

test('R04: Kiểm tra chứng từ Nợ 100 / Có 90 hiện lỗi chi tiết, không bị thông báo chung ghi đè', async () => {
  const today = new Date(); const d = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  const v = await call('POST', '/vouchers', { voucherType: 'GJ', voucherDate: d, postingDate: d, description: 'E2E lệch Nợ/Có',
    lines: [{ accountId: ctx.A('EXP'), side: 'D', amount: '100' }, { accountId: ctx.A('AP'), side: 'C', amount: '90', partnerId: ctx.P('NCC01') }] }, ctx.preparerToken, ctx.company)
  const errs = await call('POST', `/vouchers/${v.id}/validate`, null, ctx.preparerToken, ctx.company)
  assert.ok(errs.length > 0, 'API phải báo lỗi cân đối')
  const page = await open('ketoan')
  await page.click('.sidebar button:has-text("Tổng hợp")'); await page.waitForTimeout(700)
  await page.click('[role=tab]:has-text("Chứng từ kế toán")'); await page.waitForTimeout(1000)
  await page.locator('tr', { hasText: v.number }).first().click(); await page.waitForTimeout(500)
  await page.click('button:has-text("Kiểm tra")'); await page.waitForTimeout(1000)
  const msg = page.locator('.msg.error')
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
  await preparer.click('.sidebar button:has-text("Quản lý chu trình")'); await preparer.waitForTimeout(1200)
  assert.match(await worklistRow(preparer).textContent(), /Cần quyền duyệt/)
  await preparer.close()

  const approver = await open('ktt')
  await approver.click('.sidebar button:has-text("Quản lý chu trình")'); await approver.waitForTimeout(1200)
  const row = worklistRow(approver)
  assert.match(await row.textContent(), /Làm được ngay/)
  await row.locator('button:has-text("Mở")').click(); await approver.waitForTimeout(1500)
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
    await page.click(`.sidebar button:has-text("${module}")`); await page.waitForTimeout(700)
    await page.click(`[role=tab]:has-text("${tab}")`); await page.waitForTimeout(1000)
    const body = await page.locator('main').textContent()
    assert.ok(body.includes(mine), `${module}: phải thấy ${mine}`)
    assert.ok(!body.includes(other), `${module}: không được thấy ${other}`)
    assert.equal(await page.locator('.toolbar button:has-text("Đơn bán"), .toolbar button:has-text("Đơn mua")').count(), 0, `${module}: không còn nút chuyển bán/mua`)
  }
  await page.close()
})
