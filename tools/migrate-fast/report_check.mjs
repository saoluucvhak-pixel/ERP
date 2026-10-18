// Runs the TT 99/2025 statement templates (legal/TT99-2025/mau-*.json) on a HAK company through the API and prints the key
// lines and every check — a test on real data, nothing is verified or snapshotted.
//
//   node report_check.mjs <credentials.json> <from> <to> [template.json …]
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const [credFile, from, to, ...files] = process.argv.slice(2)
const cred = JSON.parse(readFileSync(credFile, 'utf8'))
const BASE = cred.api ?? 'http://127.0.0.1:5080/api'
const preparer = cred.preparer ?? { userName: 'ketoan', password: cred.users?.ketoan }
const approver = cred.approver ?? { userName: 'ktt', password: cred.users?.ktt }
let company = cred.company
async function call(token, method, path, body) {
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }
  if (company) headers['X-Company-Id'] = company
  const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await res.text()
  if (!res.ok) throw new Error(`${method} ${path} → ${res.status} ${text.slice(0, 600)}`)
  return text ? JSON.parse(text) : null
}
const login = async u => (await fetch(BASE + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ userName: u.userName, password: u.password }) }).then(r => r.json())).token
const p = await login(preparer), a = await login(approver)
if (company && !/^[0-9a-f-]{36}$/i.test(company)) {
  const code = company; company = undefined
  const hit = (await call(p, 'GET', '/me/companies')).find(c => (c.code ?? c.companyCode) === code)
  company = hit.id ?? hit.companyId
}
const legal = join(import.meta.dirname, '..', '..', 'legal', 'TT99-2025')
const fmt = v => (v === null || v === undefined ? '' : Number(v).toLocaleString('vi-VN'))
for (const f of files.length ? files : ['mau-B01-DN.json', 'mau-B02-DN.json', 'mau-B03-DN.json']) {
  const def = await call(p, 'POST', '/report-definitions', JSON.parse(readFileSync(join(legal, f), 'utf8')))
  const run = await call(a, 'POST', '/report-runs', { definitionId: def.id, fromDate: from, toDate: to, compareFrom: null, compareTo: null })
  console.log(`\n== ${run.code} ${from} → ${to}`)
  for (const l of run.lines) {
    const v = l.values ? l.values.map(fmt).join(' | ') : fmt(l.value)
    if (l.error) console.log(`  ${l.code} LỖI: ${l.error}`)
    else if (process.env.ALL ? (l.value ?? 0) != 0 : (l.bold || ['01', '11', '50', '60', '110', '131', '141', '311', '411', '420', '421B', '70'].includes(l.code)))
      console.log(`  ${l.code.padEnd(5)} ${l.name.slice(0, 60).padEnd(60)} ${v}${process.env.ALL ? '  ← ' + l.contributions.map(c => `${c.source} ${fmt(c.amount)}`).join(', ') : ''}`)
  }
  for (const c of run.checks) console.log(`  ${c.passed ? '✓' : '✗'} ${c.rule}: ${fmt(c.left)} / ${fmt(c.right)}${c.error ? ' — ' + c.error : ''}`)
}
