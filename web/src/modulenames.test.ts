import test from 'node:test'
import assert from 'node:assert/strict'
import { readdirSync, statSync } from 'node:fs'
import { join, parse } from 'node:path'
import { fileURLToPath } from 'node:url'

// Windows/macOS phân giải tên tệp không phân biệt hoa/thường: `./QuickAdd` có thể rơi vào `quickadd.ts`
// (TS2305 khi build, trang trắng khi chạy Vite). Hai module cùng thư mục không được trùng tên sau khi bỏ phần mở rộng và hạ chữ thường.
function walk(dir: string, out: string[] = []): string[] {
  for (const n of readdirSync(dir)) {
    const p = join(dir, n)
    if (statSync(p).isDirectory()) walk(p, out); else out.push(p)
  }
  return out
}

test('không có hai module trùng tên khi không phân biệt hoa/thường', () => {
  const seen = new Map<string, string>()
  const clash: string[] = []
  for (const f of walk(fileURLToPath(new URL('.', import.meta.url)))) {
    const { dir, name, ext } = parse(f)
    if (!['.ts', '.tsx', '.js', '.jsx'].includes(ext) || /\.(test|d)$/.test(name)) continue
    const key = join(dir, name).toLowerCase()
    const prev = seen.get(key)
    if (prev) clash.push(`${prev} ↔ ${f}`); else seen.set(key, f)
  }
  assert.deepEqual(clash, [])
})
