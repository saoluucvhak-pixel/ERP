// Imports the files written by fast_export.py into a HAK company through the API, posts them (maker ≠ checker) and
// reconciles HAK's trial balance month by month with can-doi-<year>.csv.
//
//   node hak_import.mjs <export folder> <year[,year…]> <credentials.json>   (several years: opening balance of the first one,
//   then every year's vouchers; fiscal years are created when missing)
//
// credentials.json: { "api": "http://127.0.0.1:5080/api", "company": "<company id>",
//                     "preparer": { "userName": "...", "password": "..." }, "approver": { "userName": "...", "password": "..." } }
// (web/.dev-credentials.json — { api, company, users: { ketoan, ktt } } — is accepted too.) Passwords are never printed.
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const [dir, yearArg, credFile] = process.argv.slice(2)
const years = yearArg.split(',').map(Number)
const year = years[0]
const cred = JSON.parse(readFileSync(credFile, 'utf8'))
const preparer = cred.preparer ?? { userName: 'ketoan', password: cred.users?.ketoan }
const approver = cred.approver ?? { userName: 'ktt', password: cred.users?.ktt }
const BASE = cred.api ?? 'http://127.0.0.1:5080/api'

async function call(token, method, path, body) {
  const headers = { 'Content-Type': 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (cred.company) headers['X-Company-Id'] = cred.company
  const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await res.text()
  if (!res.ok) throw new Error(`${method} ${path} → ${res.status} ${text.slice(0, 800)}`)
  return text ? JSON.parse(text) : null
}
const login = async u => (await call(null, 'POST', '/auth/login', { userName: u.userName, password: u.password })).token

/** RFC-4180 CSV (quotes, commas, BOM). */
function csv(name) {
  const text = readFileSync(join(dir, name), 'utf8').replace(/^﻿/, '')
  const rows = []; let row = [], cell = '', q = false
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (q) { if (c === '"' && text[i + 1] === '"') { cell += '"'; i++ } else if (c === '"') q = false; else cell += c }
    else if (c === '"') q = true
    else if (c === ',') { row.push(cell); cell = '' }
    else if (c === '\n') { row.push(cell.replace(/\r$/, '')); rows.push(row); row = []; cell = '' }
    else cell += c
  }
  if (cell || row.length) { row.push(cell); rows.push(row) }
  return rows.slice(1).filter(r => r.some(x => x.trim() !== ''))
}
const nz = s => (s === undefined || s === '' ? null : s)

async function importStep(token, label, path, rows, extra = {}) {
  const check = await call(token, 'POST', path, { ...extra, rows, commit: false })
  if (check.issues.length) throw new Error(`${label}: ${check.issues.length} lỗi, vd ${JSON.stringify(check.issues.slice(0, 5))}`)
  const r = await call(token, 'POST', path, { ...extra, rows, commit: true })
  console.log(`${label}: ${r.valid} hợp lệ, ${r.skipped} đã có${r.voucherNumber ? `, chứng từ ${r.voucherNumber}` : ''}`)
  return r
}

const p = await login(preparer), a = await login(approver)
if (cred.company && !/^[0-9a-f-]{36}$/i.test(cred.company)) {      // a company code: resolve it among the preparer's companies
  const companies = await (async () => { const c = cred.company; delete cred.company; const l = await call(p, 'GET', '/me/companies'); cred.company = c; return l })()
  const hit = companies.find(c => c.code === cred.company || c.companyCode === cred.company)
  if (!hit) throw new Error(`Không thấy công ty ${cred.company} trong danh sách của người lập.`)
  cred.company = hit.id ?? hit.companyId
}
for (const y of years) {                                            // fiscal years needed by the data (already there → ignored)
  try { await call(a, 'POST', '/fiscal-years', { code: String(y), startDate: `${y}-01-01`, endDate: `${y}-12-31` }); console.log(`Đã tạo năm tài chính ${y}`) }
  catch (e) { if (!/DUPLICATE|đã có|tồn tại|OVERLAP|trùng/i.test(String(e))) throw e }
}
await importStep(p, 'Hệ thống TK', '/import/accounts', csv('he-thong-tk.csv').map((r, i) => ({ line: i + 2, code: r[0], name: r[1], parentCode: nz(r[2]),
  isPostable: nz(r[3]), requiresPartner: nz(r[4]), openItem: nz(r[5]), cashKind: nz(r[6]), negativePolicy: nz(r[7]), balanceDisplay: nz(r[8]), reportTags: nz(r[9]) })))
await importStep(p, 'Đối tượng', '/import/partners', csv('doi-tuong.csv').map((r, i) => ({ line: i + 2, code: r[0], name: r[1], roles: nz(r[2]), taxCode: nz(r[3]) })))
const opening = await importStep(p, 'Số dư đầu năm', '/import/opening-balances', csv(`so-du-dau-ky-${year}.csv`).map((r, i) => ({ line: i + 2,
  accountCode: r[0], partnerCode: nz(r[1]), debit: nz(r[2]), credit: nz(r[3]), description: nz(r[4]) })), { date: `${year}-01-01`, description: `Số dư đầu năm ${year} chuyển từ FAST` })
await call(p, 'POST', `/vouchers/${opening.voucherId}/submit`)
await call(a, 'POST', `/vouchers/${opening.voucherId}/approve`)
await call(a, 'POST', `/vouchers/${opening.voucherId}/post`)
console.log('Số dư đầu năm: đã ghi sổ')
for (const year of years) await importStep(p, `Chứng từ ${year}`, '/import/vouchers', csv(`chung-tu-${year}.csv`).map((r, i) => ({ line: i + 2, key: r[0], date: r[1], number: nz(r[2]),
  description: nz(r[3]), accountCode: r[4], partnerCode: nz(r[5]), debit: nz(r[6]), credit: nz(r[7]), currency: nz(r[8]), foreignAmount: nz(r[9]), lineDescription: nz(r[10]), type: nz(r[11]) })))
// Rounds of submit → approve-and-post: a payment that would overdraw a cash fund before the same day's receipt is posted waits
// for the next round; stop when a round posts nothing.
for (let round = 1, posted = -1; posted !== 0; round++) {
  const sub = await call(p, 'POST', '/import/vouchers/submit')
  const post = await call(a, 'POST', '/import/vouchers/approve-post')
  posted = post.done
  console.log(`Vòng ${round}: gửi duyệt ${sub.done}, duyệt + ghi sổ ${post.done}${sub.failures.length ? `, còn chờ ${sub.failures.length}` : ''}`)
  if (posted === 0 && (sub.failures.length || post.failures.length)) console.log(`  Không ghi được: ${[...sub.failures, ...post.failures].join(' | ')}`)
}

// ---- reconcile month by month: HAK trial balance (detail accounts) vs the old system
const expected = {}
for (const y of years) for (const [m, code, o, d, c, cl] of csv(`can-doi-${y}.csv`)) (expected[m] ??= {})[code] = { o: +o || 0, d: +d || 0, c: +c || 0, cl: +cl || 0 }
let diffs = 0
const cents = x => Math.round(Number(x) * 100)
for (const m of Object.keys(expected).sort()) {
  const [y, mm] = m.split('-').map(Number)
  const last = new Date(Date.UTC(y, mm, 0)).toISOString().slice(0, 10)
  const tb = await call(a, 'GET', `/reports/trial-balance?from=${m}-01&to=${last}`)
  const got = {}
  for (const r of tb.rows) got[r.accountCode] = { o: Number(r.openingDebit) - Number(r.openingCredit), d: Number(r.periodDebit), c: Number(r.periodCredit), cl: Number(r.closingDebit) - Number(r.closingCredit) }
  const codes = new Set([...Object.keys(expected[m]), ...Object.keys(got).filter(k => /^\d/.test(k))])
  let monthDiffs = 0
  for (const code of codes) {
    const e = expected[m][code] ?? { o: 0, d: 0, c: 0, cl: 0 }, g = got[code] ?? { o: 0, d: 0, c: 0, cl: 0 }
    for (const k of ['o', 'd', 'c', 'cl']) if (cents(e[k]) !== cents(g[k])) {
      if (monthDiffs++ < 10) console.log(`  LỆCH ${m} TK ${code} ${k}: FAST ${e[k]} / HAK ${g[k]}`)
    }
  }
  diffs += monthDiffs
  console.log(`${m}: ${monthDiffs === 0 ? 'khớp' : `${monthDiffs} chênh lệch`} (${Object.keys(expected[m]).length} TK có số liệu; HAK cân: ${tb.isBalanced})`)
}
console.log(diffs === 0 ? 'KẾT QUẢ: BCĐPS HAK khớp FAST từng tháng, từng TK.' : `KẾT QUẢ: ${diffs} chênh lệch.`)

// ---- optional: compare with a trial balance printed from FAST itself (bảng cân đối phát sinh, S04), saved as tab-separated text
//      (xls-to-tsv.ps1). Every printed account line — detail and summary (cộng dồn) — and all six columns are compared.
const fi = process.argv.indexOf('--fast-report')
if (fi > 0) {
  const [file, from, to] = process.argv.slice(fi + 1)
  const printed = readFileSync(file, 'utf8').replace(/^﻿/, '').split(/\r?\n/).map(l => l.split('\t'))
    .filter(c => /^\d[\d.]*$/.test((c[0] ?? '').trim()) && c.length >= 8)
    .map(c => ({ code: c[0].trim(), v: c.slice(2, 8).map(x => cents(x || 0)) }))
  const tb = await call(a, 'GET', `/reports/trial-balance?from=${from}&to=${to}&rollup=true`)
  const hak = {}
  for (const r of [...tb.rows, ...(tb.groups ?? [])])
    hak[r.accountCode] = [r.openingDebit, r.openingCredit, r.periodDebit, r.periodCredit, r.closingDebit, r.closingCredit].map(cents)
  const cols = ['Dư đầu Nợ', 'Dư đầu Có', 'PS Nợ', 'PS Có', 'Dư cuối Nợ', 'Dư cuối Có']
  let bad = 0
  for (const { code, v } of printed) {
    const h = hak[code] ?? [0, 0, 0, 0, 0, 0]
    v.forEach((x, i) => { if (x !== h[i]) { bad++; console.log(`  LỆCH TK ${code} ${cols[i]}: FAST ${x / 100} / HAK ${h[i] / 100}`) } })
  }
  const extra = Object.entries(hak).filter(([c, h]) => /^\d/.test(c) && !printed.some(p => p.code === c) && h.some(x => x !== 0)).map(([c]) => c)
  if (extra.length) { bad += extra.length; console.log(`  TK có số trên HAK nhưng không có trong báo cáo FAST: ${extra.join(', ')}`) }
  console.log(`So với báo cáo FAST ${from} → ${to}: ${printed.length} dòng TK, ${bad === 0 ? 'KHỚP HẾT 6 cột' : `${bad} chênh lệch`}`)
  diffs += bad
}
process.exit(diffs === 0 ? 0 : 1)
