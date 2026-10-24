// Chạy test giao diện bằng trình duyệt thật: dựng API (SQLite tạm) + dữ liệu thử + web dev rồi chạy e2e/*.e2e.mjs, cuối cùng dọn tiến trình.
// Yêu cầu: đã build API (dotnet build), .NET SDK trong PATH, trình duyệt Playwright (npx playwright install chromium).
import { spawn, spawnSync } from 'node:child_process'
import { existsSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const web = path.resolve(here, '..')
const apiPort = process.env.E2E_API_PORT ?? '15080'
const webPort = process.env.E2E_WEB_PORT ?? '15173'
const db = path.join(tmpdir(), `hak-e2e-${process.pid}.db`)
const config = process.env.E2E_CONFIG ?? 'Debug'
const children = []
const stop = () => { for (const c of children) { try { c.kill('SIGTERM') } catch { /* đã thoát */ } } for (const f of [db, db + '-wal', db + '-shm']) { try { rmSync(f) } catch { /* không có */ } } }
process.on('exit', stop); process.on('SIGINT', () => process.exit(130))

const start = (cmd, args, opts = {}) => { const c = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], ...opts }); children.push(c); c.stdout.on('data', () => {}); c.stderr.on('data', () => {}); return c }
const waitFor = async (url, what, ms = 120000) => {
  const t0 = Date.now()
  while (Date.now() - t0 < ms) { try { const r = await fetch(url); if (r.ok || r.status < 500) return } catch { /* chưa lên */ } await new Promise(r => setTimeout(r, 500)) }
  throw new Error(`${what} không lên sau ${ms / 1000}s`)
}

start('dotnet', ['run', '--no-build', '-c', config, '--project', path.resolve(web, '../src/Hak.Api'), '--urls', `http://127.0.0.1:${apiPort}`, '--Hak:Environment=Development', `--Hak:DatabasePath=${db}`])
await waitFor(`http://127.0.0.1:${apiPort}/api/health`, 'API')
const seed = spawnSync('node', ['scripts/dev-seed.mjs'], { cwd: web, env: { ...process.env, HAK_API: `http://127.0.0.1:${apiPort}/api` }, encoding: 'utf8' })
if (seed.status !== 0) { console.error(seed.stdout, seed.stderr); process.exit(1) }
start('npx', ['vite', '--host', '127.0.0.1', '--port', webPort, '--strictPort'], { cwd: web, env: { ...process.env, HAK_WEB_PORT: webPort, HAK_API_ORIGIN: `http://127.0.0.1:${apiPort}` } })
await waitFor(`http://127.0.0.1:${webPort}/`, 'web')

const files = ['e2e/ui.e2e.mjs'].filter(f => existsSync(path.join(web, f)))
const test = spawnSync('node', ['--test', '--test-concurrency=1', ...files], { cwd: web, stdio: 'inherit', env: { ...process.env, E2E_API: `http://127.0.0.1:${apiPort}/api`, E2E_WEB: `http://127.0.0.1:${webPort}` } })
process.exit(test.status ?? 1)
