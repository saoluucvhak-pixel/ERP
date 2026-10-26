// Chạy test giao diện bằng trình duyệt thật: dựng API (SQLite tạm) + dữ liệu thử + web dev rồi chạy e2e/*.e2e.mjs, cuối cùng dọn tiến trình.
// Yêu cầu: đã build API (dotnet build), .NET SDK trong PATH, trình duyệt Playwright (npx playwright install chromium)
// hoặc E2E_BROWSER_CHANNEL=chrome|msedge / E2E_BROWSER_PATH=<exe> để dùng trình duyệt có sẵn. Chạy được trên Windows và Linux.
import { spawn, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const web = path.resolve(here, '..')
const apiPort = process.env.E2E_API_PORT ?? '15080'
const webPort = process.env.E2E_WEB_PORT ?? '15173'
const db = path.join(tmpdir(), `hak-e2e-${process.pid}.db`)
const config = process.env.E2E_CONFIG ?? 'Debug'
const logDir = path.join(here, 'artifacts')
const children = []
const logs = { api: [], web: [] }
const isWin = process.platform === 'win32'

// Dọn cả cây tiến trình: `dotnet run` sinh tiến trình con mà kill() trực tiếp không tới (Windows cần taskkill /T).
const killTree = c => {
  if (c.exitCode !== null || !c.pid) return
  try { if (isWin) spawnSync('taskkill', ['/pid', String(c.pid), '/T', '/F']); else c.kill('SIGTERM') } catch { /* đã thoát */ }
}
const stop = () => { for (const c of children) killTree(c); for (const f of [db, db + '-wal', db + '-shm']) { try { rmSync(f) } catch { /* không có */ } } }
process.on('exit', stop); process.on('SIGINT', () => process.exit(130)); process.on('SIGTERM', () => process.exit(143))

const dumpLogs = () => {
  mkdirSync(logDir, { recursive: true })
  for (const [k, v] of Object.entries(logs)) writeFileSync(path.join(logDir, `${k}.log`), v.join(''))
  console.error(`Log API/web: ${logDir}`)
}
const die = msg => { console.error(msg); dumpLogs(); process.exit(1) }

const start = (name, cmd, args, opts = {}) => {
  const c = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], ...opts })
  c.stdout.on('data', d => logs[name].push(String(d))); c.stderr.on('data', d => logs[name].push(String(d)))
  c.exited = new Promise(res => c.on('exit', code => res(code))); c.on('error', e => { logs[name].push(`spawn error: ${e.message}\n`); c.spawnError = e })
  children.push(c)
  return c
}
// Đợi cho tới khi `ok(response)` đúng; tiến trình thoát sớm hoặc không khởi động được thì thất bại ngay, không đợi hết giờ.
const waitFor = async (url, what, child, ok, ms = 120000) => {
  const t0 = Date.now()
  while (Date.now() - t0 < ms) {
    if (child.spawnError || child.exitCode !== null) die(`${what} thoát sớm (mã ${child.exitCode ?? child.spawnError?.message}).\n${logs[child.name ?? ''] ?? ''}`)
    try { const r = await fetch(url); if (await ok(r)) return } catch { /* chưa lên */ }
    await new Promise(r => setTimeout(r, 300))
  }
  die(`${what} không lên sau ${ms / 1000}s`)
}

// Optional isolated build output: allows testing precisely the same artifacts as the full backend suite.
const artifactArgs = process.env.E2E_ARTIFACTS_PATH ? ['--artifacts-path', path.resolve(process.env.E2E_ARTIFACTS_PATH)] : []
const api = start('api', 'dotnet', ['run', '--no-build', '-c', config, '--project', path.resolve(web, '../src/Hak.Api'), ...artifactArgs, '--urls', `http://127.0.0.1:${apiPort}`, '--Hak:Environment=Development', '--Hak:EInvoice:Provider=Simulated', `--Hak:DatabasePath=${db}`])
api.name = 'api'
await waitFor(`http://127.0.0.1:${apiPort}/api/health`, 'API', api, r => r.ok)
const seed = spawnSync(process.execPath, ['scripts/dev-seed.mjs'], { cwd: web, env: { ...process.env, HAK_API: `http://127.0.0.1:${apiPort}/api` }, encoding: 'utf8' })
if (seed.status !== 0) die(`${seed.stdout}\n${seed.stderr}`)

// Vite gọi thẳng bằng Node + CLI JavaScript (Windows dùng shim npx.cmd nên spawn('npx') báo ENOENT).
const viteCli = path.join(path.dirname(createRequire(path.join(web, 'package.json')).resolve('vite/package.json')), 'bin', 'vite.js')
const vite = start('web', process.execPath, [viteCli, '--host', '127.0.0.1', '--port', webPort, '--strictPort'], { cwd: web, env: { ...process.env, HAK_WEB_PORT: webPort, HAK_API_ORIGIN: `http://127.0.0.1:${apiPort}` } })
vite.name = 'web'
await waitFor(`http://127.0.0.1:${webPort}/`, 'web', vite, async r => r.ok && (await r.text()).includes('<div id="root"'))
// Cổng proxy tới API phải thông, nếu không web lên nhưng mọi gọi API đều lỗi.
await waitFor(`http://127.0.0.1:${webPort}/api/health`, 'proxy web→API', vite, r => r.ok)

const files = ['e2e/ui.e2e.mjs'].filter(f => existsSync(path.join(web, f)))
const test = spawnSync(process.execPath, ['--test', '--test-concurrency=1', ...files], { cwd: web, stdio: 'inherit', env: { ...process.env, E2E_API: `http://127.0.0.1:${apiPort}/api`, E2E_WEB: `http://127.0.0.1:${webPort}` } })
if (test.status !== 0) dumpLogs()
process.exit(test.status ?? 1)
